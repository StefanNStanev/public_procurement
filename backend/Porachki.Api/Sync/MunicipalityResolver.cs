using System.Text.Json;
using System.Text.RegularExpressions;

namespace Porachki.Api.Sync;

/// <summary>
/// Определя общината на възложителя по: (1) име „Община X“, (2) пощенски код,
/// (3) населено място + област. Използва ЕКАТТЕ данни (Data/Seed/settlements.json, MIT).
/// </summary>
public partial class MunicipalityResolver
{
    public record Settlement(string Ekatte, string Name, string? Type, string Municipality, string Oblast, string[] PostCodes);
    public record Result(string? Municipality, string? Oblast, string? City);

    private readonly Dictionary<string, List<Settlement>> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Settlement>> _byPostCode = new();
    private readonly Dictionary<string, string> _municipalityOblast = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>NUTS3 код → област.</summary>
    private static readonly Dictionary<string, string> Nuts3 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BG311"] = "Видин", ["BG312"] = "Монтана", ["BG313"] = "Враца", ["BG314"] = "Плевен", ["BG315"] = "Ловеч",
        ["BG321"] = "Велико Търново", ["BG322"] = "Габрово", ["BG323"] = "Русе", ["BG324"] = "Разград", ["BG325"] = "Силистра",
        ["BG331"] = "Варна", ["BG332"] = "Добрич", ["BG333"] = "Шумен", ["BG334"] = "Търговище",
        ["BG341"] = "Бургас", ["BG342"] = "Сливен", ["BG343"] = "Ямбол", ["BG344"] = "Стара Загора",
        ["BG411"] = "София", ["BG412"] = "София област", ["BG413"] = "Благоевград", ["BG414"] = "Перник", ["BG415"] = "Кюстендил",
        ["BG421"] = "Пловдив", ["BG422"] = "Хасково", ["BG423"] = "Пазарджик", ["BG424"] = "Смолян", ["BG425"] = "Кърджали",
    };

    public MunicipalityResolver()
    {
        using var stream = typeof(MunicipalityResolver).Assembly.GetManifestResourceStream("Porachki.Seed.settlements.json")
            ?? throw new InvalidOperationException("Липсва вграденият ресурс settlements.json");
        using var doc = JsonDocument.Parse(stream);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var s = new Settlement(
                e.GetProperty("e").GetString()!,
                e.GetProperty("n").GetString()!,
                e.TryGetProperty("t", out var t) ? t.GetString() : null,
                e.GetProperty("m").GetString()!,
                e.GetProperty("o").GetString()!,
                e.GetProperty("p").EnumerateArray().Select(x => x.GetString()!).ToArray());

            Add(_byName, Normalize(s.Name), s);
            foreach (var pc in s.PostCodes) Add(_byPostCode, pc, s);
            _municipalityOblast.TryAdd(s.Municipality, s.Oblast);
        }
    }

    public IReadOnlyDictionary<string, string> MunicipalityOblasts => _municipalityOblast;

    public Result Resolve(string? buyerName, string? locality, string? postalCode, string? region)
    {
        var oblast = OblastFrom(region);
        var city = CleanLocality(locality);

        // 1) „Община Пловдив“, „Столична община“, „Община Марица - Пловдив“
        var fromName = MunicipalityFromBuyerName(buyerName);
        if (fromName is not null)
            return new Result(fromName, _municipalityOblast.GetValueOrDefault(fromName) ?? oblast, city);

        // 2) Пощенски код
        if (!string.IsNullOrWhiteSpace(postalCode))
        {
            var pc = DigitsOnly().Replace(postalCode, "");
            if (_byPostCode.TryGetValue(pc, out var list))
            {
                var munis = Filter(list, oblast, city).Select(s => s.Municipality).Distinct().ToList();
                if (munis.Count == 1) return new Result(munis[0], _municipalityOblast[munis[0]], city);
            }
        }

        // 3) Населено място (+ област за еднозначност)
        if (city is not null)
        {
            if (Normalize(city) == "софия") return new Result("Столична", "София", "София");
            if (_byName.TryGetValue(Normalize(city), out var list))
            {
                var candidates = oblast is null ? list : list.Where(s => s.Oblast.Equals(oblast, StringComparison.OrdinalIgnoreCase)).ToList();
                // Предпочитаме градове пред села със същото име
                var towns = candidates.Where(s => s.Type == "гр.").ToList();
                if (towns.Count > 0) candidates = towns;
                var munis = candidates.Select(s => s.Municipality).Distinct().ToList();
                if (munis.Count == 1) return new Result(munis[0], _municipalityOblast[munis[0]], city);
            }
        }

        return new Result(null, oblast, city);
    }

    private string? MunicipalityFromBuyerName(string? buyerName)
    {
        if (string.IsNullOrWhiteSpace(buyerName)) return null;
        var n = buyerName.Trim().Trim('"', '„', '“');
        if (n.StartsWith("Столична община", StringComparison.OrdinalIgnoreCase)) return "Столична";
        var m = MunicipalityName().Match(n);
        if (!m.Success) return null;
        var name = m.Groups[1].Value.Trim();
        return _municipalityOblast.ContainsKey(name) ? _municipalityOblast.Keys.First(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) : null;
    }

    private static IEnumerable<Settlement> Filter(List<Settlement> list, string? oblast, string? city)
    {
        IEnumerable<Settlement> r = list;
        if (oblast is not null) { var f = r.Where(s => s.Oblast.Equals(oblast, StringComparison.OrdinalIgnoreCase)).ToList(); if (f.Count > 0) r = f; }
        if (city is not null) { var f = r.Where(s => Normalize(s.Name) == Normalize(city)).ToList(); if (f.Count > 0) r = f; }
        return r;
    }

    private static string? OblastFrom(string? region)
    {
        if (string.IsNullOrWhiteSpace(region)) return null;
        var r = region.Trim();
        if (Nuts3.TryGetValue(r, out var o)) return o;
        r = r.Replace("област", "", StringComparison.OrdinalIgnoreCase).Replace("обл.", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (r.Contains("столица", StringComparison.OrdinalIgnoreCase) || r.Equals("София-град", StringComparison.OrdinalIgnoreCase)) return "София";
        if (r.StartsWith("София", StringComparison.OrdinalIgnoreCase) && r.Length > 5) return "София област";
        return Nuts3.Values.FirstOrDefault(v => v.Equals(r, StringComparison.OrdinalIgnoreCase));
    }

    public static string? CleanLocality(string? locality)
    {
        if (string.IsNullOrWhiteSpace(locality)) return null;
        var s = LocalityPrefix().Replace(locality.Trim(), "").Trim().Trim('"', '„', '“', ',').Trim();
        return s.Length == 0 ? null : s;
    }

    private static string Normalize(string s) => WhiteSpace().Replace(s.Trim().ToLowerInvariant(), " ");

    private static void Add(Dictionary<string, List<Settlement>> d, string key, Settlement s)
    {
        if (!d.TryGetValue(key, out var list)) d[key] = list = [];
        list.Add(s);
    }

    [GeneratedRegex(@"^Община\s+([\p{L}\s\-]+?)(?:\s*[-–,(].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex MunicipalityName();

    [GeneratedRegex(@"^(гр\.|град|с\.|село|к\.к\.|кв\.)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LocalityPrefix();

    [GeneratedRegex(@"\D")]
    private static partial Regex DigitsOnly();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
