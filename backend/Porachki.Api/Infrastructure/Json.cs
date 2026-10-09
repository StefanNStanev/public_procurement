using System.Globalization;
using System.Text.Json;

namespace Porachki.Api.Infrastructure;

/// <summary>Толерантни помощници за четене на JSON с непостоянна структура (OCDS, TED).</summary>
public static class Json
{
    public static JsonElement? Prop(this JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        return el.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;
    }

    public static JsonElement? Path(this JsonElement el, params string[] path)
    {
        JsonElement? cur = el;
        foreach (var p in path)
        {
            cur = cur?.Prop(p);
            if (cur is null) return null;
        }
        return cur;
    }

    public static string? Str(this JsonElement? el) => el is { } e ? e.Str() : null;

    public static string? Str(this JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => NullIfEmpty(el.GetString()),
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => el.EnumerateArray().Select(x => x.Str()).FirstOrDefault(x => x is not null),
        _ => null,
    };

    public static IEnumerable<JsonElement> Items(this JsonElement? el)
    {
        if (el is not { } e) yield break;
        if (e.ValueKind == JsonValueKind.Array)
            foreach (var x in e.EnumerateArray()) yield return x;
        else if (e.ValueKind != JsonValueKind.Null && e.ValueKind != JsonValueKind.Undefined)
            yield return e;
    }

    public static decimal? Dec(this JsonElement? el)
    {
        if (el is not { } e) return null;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDecimal(out var d)) return d;
        if (e.ValueKind == JsonValueKind.Array) return e.EnumerateArray().Select(x => ((JsonElement?)x).Dec()).FirstOrDefault(x => x is not null);
        var s = e.Str();
        return s is not null && decimal.TryParse(s.Replace(" ", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var r) ? r : null;
    }

    public static DateTime? Date(this JsonElement? el) => ParseDate(el.Str());

    public static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return dto.UtcDateTime;
        // TED: "2026-10-20+02:00" (дата със зона, без час)
        if (s.Length > 10 && DateTime.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            var offset = TimeSpan.Zero;
            var tz = s[10..];
            if (tz != "Z" && TimeSpan.TryParse(tz.TrimStart('+', '-'), CultureInfo.InvariantCulture, out var ts))
                offset = tz.StartsWith('-') ? -ts : ts;
            // Краен срок без час → края на деня по местно време
            return new DateTimeOffset(d.AddDays(1).AddSeconds(-1), offset).UtcDateTime;
        }
        return null;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
