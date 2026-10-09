using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Porachki.Api.Domain;
using Porachki.Api.Infrastructure;

namespace Porachki.Api.Sync.Eop;

/// <summary>
/// Чете дневните OCDS файлове с отворени данни на ЦАИС ЕОП
/// (https://storage.eop.bg/open-data-YYYY-MM-DD/). Поддържа JSON, ZIP и GZIP,
/// както и release/record пакети.
/// </summary>
public partial class EopOcdsSource(HttpClient http, IOptions<SyncOptions> options, MunicipalityResolver resolver, ILogger<EopOcdsSource> log)
{
    private readonly EopOptions _opt = options.Value.Eop;

    public record DayResult(bool Found, int Files);

    /// <summary>Обработва всички файлове за деня и подава нормализираните записи на <paramref name="sink"/>.</summary>
    public async Task<DayResult> ReadDayAsync(DateOnly day, Func<IReadOnlyList<TenderUpsert>, Task> sink, CancellationToken ct)
    {
        var files = await DiscoverFilesAsync(day, ct);
        if (files is null) return new DayResult(false, 0);

        foreach (var file in files)
        {
            log.LogInformation("ЦАИС ЕОП: чета {Url}", file);
            var releases = await LoadReleasesAsync(file, ct);
            var batch = new List<TenderUpsert>(500);
            foreach (var r in releases)
            {
                var t = Map(r);
                if (t is null) continue;
                batch.Add(t);
                if (batch.Count >= 500) { await sink(batch); batch = new List<TenderUpsert>(500); }
            }
            if (batch.Count > 0) await sink(batch);
        }
        return new DayResult(true, files.Count);
    }

    /// <summary>Връща списък с файлове за деня или null, ако данните още не са публикувани.</summary>
    private async Task<List<Uri>?> DiscoverFilesAsync(DateOnly day, CancellationToken ct)
    {
        var url = new Uri(_opt.DailyFolderUrlTemplate.Replace("{date}", day.ToString("yyyy-MM-dd")));
        if (LooksLikeDataFile(url.AbsolutePath)) return [url];

        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) return null;
        resp.EnsureSuccessStatusCode();

        var mediaType = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("json") || mediaType.Contains("zip") || mediaType.Contains("octet-stream") || mediaType.Contains("gzip"))
            return [url]; // папката директно връща данните

        var body = await resp.Content.ReadAsStringAsync(ct);
        var links = new List<Uri>();

        if (body.Contains("<ListBucketResult", StringComparison.Ordinal))
        {
            // S3/MinIO листинг
            foreach (Match m in S3Key().Matches(body))
            {
                var key = WebUtility.HtmlDecode(m.Groups[1].Value);
                if (LooksLikeDataFile(key)) links.Add(new Uri(url, key));
            }
        }
        else
        {
            // HTML листинг (nginx/apache autoindex и т.н.)
            foreach (Match m in Href().Matches(body))
            {
                var href = WebUtility.HtmlDecode(m.Groups[1].Value);
                if (LooksLikeDataFile(href)) links.Add(new Uri(url, href));
            }
        }

        if (links.Count == 0)
        {
            log.LogWarning("ЦАИС ЕОП: {Url} не съдържа разпознаваеми файлове. Проверете Sync:Eop:DailyFolderUrlTemplate.", url);
            return null;
        }
        return links.Distinct().ToList();
    }

    private static bool LooksLikeDataFile(string path)
    {
        var p = path.Split('?')[0].ToLowerInvariant();
        return p.EndsWith(".json") || p.EndsWith(".zip") || p.EndsWith(".gz") || p.EndsWith(".jsonl");
    }

    private async Task<List<JsonElement>> LoadReleasesAsync(Uri file, CancellationToken ct)
    {
        await using var tmp = new FileStream(System.IO.Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        using (var resp = await http.GetAsync(file, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await src.CopyToAsync(tmp, ct);
        }
        tmp.Position = 0;

        var head = new byte[2];
        _ = await tmp.ReadAsync(head, ct);
        tmp.Position = 0;

        var result = new List<JsonElement>();
        if (head[0] == 'P' && head[1] == 'K')
        {
            using var zip = new ZipArchive(tmp, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                await using var es = entry.Open();
                await ParseInto(es, result, ct);
            }
        }
        else if (head[0] == 0x1f && head[1] == 0x8b)
        {
            await using var gz = new GZipStream(tmp, CompressionMode.Decompress, leaveOpen: true);
            await ParseInto(gz, result, ct);
        }
        else
        {
            await ParseInto(tmp, result, ct);
        }
        log.LogInformation("ЦАИС ЕОП: {Count} releases в {Url}", result.Count, file);
        return result;
    }

    private static async Task ParseInto(Stream s, List<JsonElement> output, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(s, new JsonDocumentOptions { AllowTrailingCommas = true }, ct);
        Collect(doc.RootElement, output, 0);
    }

    /// <summary>Намира OCDS releases в произволна обвивка (пакет, records, масив, JSON-в-низ).</summary>
    private static void Collect(JsonElement el, List<JsonElement> output, int depth)
    {
        if (depth > 6) return;
        switch (el.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var x in el.EnumerateArray()) Collect(x, output, depth + 1);
                break;
            case JsonValueKind.String:
                var str = el.GetString();
                if (str is { Length: > 1 } && (str[0] == '{' || str[0] == '['))
                {
                    try
                    {
                        using var inner = JsonDocument.Parse(str);
                        Collect(inner.RootElement.Clone(), output, depth + 1);
                    }
                    catch (JsonException) { /* не е JSON */ }
                }
                break;
            case JsonValueKind.Object:
                if (el.TryGetProperty("ocid", out _) && (el.TryGetProperty("tender", out _) || el.TryGetProperty("tag", out _)))
                {
                    output.Add(el.Clone());
                    break;
                }
                if (el.TryGetProperty("releases", out var rels)) Collect(rels, output, depth + 1);
                if (el.TryGetProperty("records", out var recs))
                    foreach (var rec in recs.EnumerateArray())
                    {
                        if (rec.TryGetProperty("compiledRelease", out var cr)) output.Add(cr.Clone());
                        else if (rec.TryGetProperty("releases", out var rr)) Collect(rr, output, depth + 1);
                    }
                break;
        }
    }

    private TenderUpsert? Map(JsonElement r)
    {
        var ocid = r.Prop("ocid").Str();
        if (ocid is null) return null;

        var tender = r.Prop("tender");
        var lots = tender?.Prop("lots").Items().ToList() ?? [];

        var title = tender?.Prop("title").Str() ?? lots.Select(l => l.Prop("title").Str()).FirstOrDefault(x => x is not null);

        // CPV
        var cpvCodes = new List<string>();
        var cpvNames = new Dictionary<string, string>();
        void AddCpv(JsonElement? c)
        {
            if (c is not { } cl) return;
            var scheme = cl.Prop("scheme").Str();
            if (scheme is not null && !scheme.Contains("CPV", StringComparison.OrdinalIgnoreCase)) return;
            var id = NormalizeCpv(cl.Prop("id").Str());
            if (id is null) return;
            if (!cpvCodes.Contains(id)) cpvCodes.Add(id);
            var desc = cl.Prop("description").Str();
            if (desc is not null) cpvNames.TryAdd(id, desc);
        }
        AddCpv(tender?.Prop("classification"));
        foreach (var item in tender?.Prop("items").Items() ?? [])
        {
            AddCpv(item.Prop("classification"));
            foreach (var ac in item.Prop("additionalClassifications").Items()) AddCpv(ac);
        }

        // Възложител
        var buyerId = r.Path("buyer", "id").Str();
        var buyerName = r.Path("buyer", "name").Str();
        JsonElement? buyerParty = null;
        foreach (var p in r.Prop("parties").Items())
        {
            var isBuyer = (buyerId is not null && p.Prop("id").Str() == buyerId)
                          || p.Prop("roles").Items().Any(x => x.Str() is "buyer" or "procuringEntity");
            if (isBuyer) { buyerParty = p; break; }
        }
        buyerName ??= buyerParty?.Prop("name").Str();
        var addr = buyerParty?.Prop("address");
        var place = resolver.Resolve(buyerName, addr?.Prop("locality").Str(), addr?.Prop("postalCode").Str(), addr?.Prop("region").Str());

        // Срок за оферти
        var deadline = tender?.Path("tenderPeriod", "endDate").Date()
                       ?? lots.Select(l => l.Path("tenderPeriod", "endDate").Date()).Where(d => d is not null).Min();

        // Стойност
        var amount = tender?.Path("value", "amount").Dec();
        var currency = tender?.Path("value", "currency").Str();
        if (amount is null && lots.Count > 0)
        {
            var lotValues = lots.Select(l => (A: l.Path("value", "amount").Dec(), C: l.Path("value", "currency").Str())).Where(x => x.A is not null).ToList();
            if (lotValues.Count > 0) { amount = lotValues.Sum(x => x.A); currency = lotValues[0].C; }
        }

        // Вид процедура: детайлът е на български („Открита процедура“, „Публично състезание“…)
        var methodDetails = tender?.Prop("procurementMethodDetails").Str();
        var method = tender?.Prop("procurementMethod").Str();
        string? procKey = null, procLabel = null;
        if (methodDetails is not null) { procLabel = Capitalize(methodDetails); procKey = "bg:" + procLabel.ToLowerInvariant(); }
        else if (method is not null) { procKey = method; procLabel = Labels.Procedures.GetValueOrDefault(method, method); }

        var tenderNumber = tender?.Prop("id").Str();
        var displayId = tenderNumber is not null && tenderNumber.All(char.IsDigit) ? tenderNumber : TrailingNumber().Match(ocid).Value;

        var releaseDate = r.Prop("date").Date();
        var tags = r.Prop("tag").Items().Select(x => x.Str()).ToList();
        var published = tender?.Path("tenderPeriod", "startDate").Date()
                        ?? (tags.Contains("tender") ? releaseDate : null);

        return new TenderUpsert
        {
            Source = TenderSource.Bg,
            ExternalId = ocid,
            NoticeNumber = displayId,
            Title = title,
            Description = tender?.Prop("description").Str(),
            BuyerName = buyerName,
            ContractType = NormalizeCategory(tender?.Prop("mainProcurementCategory").Str()),
            ProcedureKey = procKey,
            ProcedureLabel = procLabel,
            CpvCodes = cpvCodes,
            CpvNames = cpvNames,
            CountryCode = "BG",
            Region = place.Oblast,
            Municipality = place.Municipality,
            City = place.City,
            PublishedAt = published,
            Deadline = deadline,
            EstimatedValue = amount,
            Currency = currency?.ToUpperInvariant(),
            Status = tender?.Prop("status").Str(),
            Url = string.IsNullOrEmpty(displayId) ? null : _opt.TenderUrlTemplate.Replace("{id}", displayId),
            SourceUpdatedAt = releaseDate,
        };
    }

    public static string? NormalizeCpv(string? code)
    {
        if (code is null) return null;
        var digits = new string(code.TakeWhile(c => c != '-').Where(char.IsDigit).ToArray());
        return digits.Length >= 8 ? digits[..8] : null;
    }

    private static string? NormalizeCategory(string? c) => c?.ToLowerInvariant() switch
    {
        "goods" or "supplies" => "goods",
        "services" => "services",
        "works" => "works",
        _ => null,
    };

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    [GeneratedRegex(@"<Key>(.*?)</Key>")]
    private static partial Regex S3Key();

    [GeneratedRegex(@"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex Href();

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingNumber();
}
