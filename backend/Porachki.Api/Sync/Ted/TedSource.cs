using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Porachki.Api.Domain;
using Porachki.Api.Infrastructure;
using Porachki.Api.Sync.Eop;

namespace Porachki.Api.Sync.Ted;

/// <summary>
/// TED Search API v3 (https://api.ted.europa.eu/v3/notices/search) – без ключ.
/// Използва scope=ACTIVE и ITERATION пагинация.
/// </summary>
public class TedSource(HttpClient http, IOptions<SyncOptions> options, ILogger<TedSource> log)
{
    private readonly TedOptions _opt = options.Value.Ted;

    /// <param name="publishedSince">null = пълно зареждане на всички активни обявления.</param>
    public async Task<(int Count, DateOnly? MaxPublished)> FetchAsync(DateOnly? publishedSince, Func<IReadOnlyList<TenderUpsert>, Task> sink, CancellationToken ct)
    {
        var query = _opt.Query;
        if (publishedSince is { } since)
            query = $"({query}) AND publication-date>={since:yyyyMMdd}";

        string? token = null;
        var total = 0;
        DateOnly? maxPublished = null;

        for (var page = 1; page <= _opt.MaxPages; page++)
        {
            var body = new JsonObject
            {
                ["query"] = query,
                ["fields"] = new JsonArray(_opt.Fields.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()),
                ["limit"] = _opt.PageSize,
                ["scope"] = "ACTIVE",
                ["paginationMode"] = "ITERATION",
                ["checkQuerySyntax"] = false,
            };
            if (token is not null) body["iterationNextToken"] = token;

            using var doc = await PostWithRetryAsync(body, ct);
            var root = doc.RootElement;
            if (page == 1)
                log.LogInformation("TED: {Total} обявления отговарят на заявката", root.Prop("totalNoticeCount").Str() ?? "?");

            var notices = root.Prop("notices").Items().ToList();
            if (notices.Count == 0) break;

            var batch = new List<TenderUpsert>(notices.Count);
            foreach (var n in notices)
            {
                var t = Map(n);
                if (t is null) continue;
                batch.Add(t);
                if (t.PublishedAt is { } p)
                {
                    var d = DateOnly.FromDateTime(p);
                    if (maxPublished is null || d > maxPublished) maxPublished = d;
                }
            }
            await sink(batch);
            total += batch.Count;

            token = root.Prop("iterationNextToken").Str();
            if (token is null) break;
            await Task.Delay(_opt.DelayMs, ct);
        }
        return (total, maxPublished);
    }

    private async Task<JsonDocument> PostWithRetryAsync(JsonObject body, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var resp = await http.PostAsJsonAsync(_opt.SearchUrl, body, ct);
            if (resp.IsSuccessStatusCode)
                return await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var text = await resp.Content.ReadAsStringAsync(ct);
            var code = (int)resp.StatusCode;
            var retryable = code == 429 || code >= 500;
            if (!retryable || attempt >= 5)
                throw new HttpRequestException($"TED {(int)resp.StatusCode}: {Truncate(text, 600)}");

            var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2);
            log.LogWarning("TED {Status}, нов опит след {Wait}s", (int)resp.StatusCode, wait.TotalSeconds);
            await Task.Delay(wait, ct);
        }
    }

    private TenderUpsert? Map(JsonElement n)
    {
        var pubNo = Text(n, "publication-number");
        if (pubNo is null) return null;

        var cpv = Values(n, "classification-cpv").Select(v => EopOcdsSource.NormalizeCpv(v.Str())).OfType<string>().Distinct().ToList();
        var nature = Text(n, "contract-nature");
        var procedure = Text(n, "procedure-type");
        var country = Countries.ToIso2(Text(n, "buyer-country"));
        var title = Text(n, "notice-title");
        // eForms BT-765 (none | fa-wo-rc | fa-w-rc | fa-mix) и BT-766 (none | dps-list | dps-nlist), по обособени позиции
        var arrangement = Values(n, "dps-usage-lot").Any(v => v.Str() is { } s && s != "none") ? Arrangement.Dps
            : Values(n, "framework-agreement-lot").Any(v => v.Str() is { } s && s != "none") ? Arrangement.Framework
            : Arrangement.FromText(title) ?? Arrangement.Single;
        var deadline = Values(n, "deadline-receipt-tender-date-lot").Select(v => Json.ParseDate(v.Str())).Where(d => d is not null).Min();

        return new TenderUpsert
        {
            Source = TenderSource.Eu,
            ExternalId = Text(n, "procedure-identifier") ?? pubNo,
            NoticeNumber = pubNo,
            Title = title,
            BuyerName = Text(n, "buyer-name"),
            ContractType = nature?.ToLowerInvariant() switch
            {
                "supplies" or "goods" => "goods",
                "services" => "services",
                "works" => "works",
                _ => null,
            },
            ProcedureKey = procedure,
            ProcedureLabel = procedure is null ? null : Labels.Procedures.GetValueOrDefault(procedure, procedure),
            Arrangement = arrangement,
            CpvCodes = cpv,
            CountryCode = country,
            City = NormalizeCity(Text(n, "buyer-city")),
            PublishedAt = Json.ParseDate(Text(n, "publication-date")),
            Deadline = deadline,
            EstimatedValue = Values(n, "estimated-value-proc").Select(v => ((JsonElement?)v).Dec()).FirstOrDefault(v => v is not null),
            Currency = Text(n, "estimated-value-cur-proc")?.ToUpperInvariant(),
            Url = _opt.NoticeUrlTemplate.Replace("{id}", pubNo),
            SourceUpdatedAt = Json.ParseDate(Text(n, "publication-date")),
        };
    }

    /// <summary>
    /// Стойностите в TED са низ, масив или многоезичен обект {"bul": [...], "eng": [...]}.
    /// Връща първата стойност според предпочитаните езици.
    /// </summary>
    private string? Text(JsonElement n, string field) => Values(n, field).Select(v => v.Str()).FirstOrDefault(s => s is not null);

    private IEnumerable<JsonElement> Values(JsonElement n, string field)
    {
        var v = n.Prop(field);
        if (v is not { } el) yield break;
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var lang in _opt.Languages)
                if (el.TryGetProperty(lang, out var lv))
                {
                    foreach (var x in ((JsonElement?)lv).Items()) yield return x;
                    yield break;
                }
            foreach (var p in el.EnumerateObject())
            {
                foreach (var x in ((JsonElement?)p.Value).Items()) yield return x;
                yield break;
            }
            yield break;
        }
        foreach (var x in v.Items()) yield return x;
    }

    private static string? NormalizeCity(string? city)
    {
        if (string.IsNullOrWhiteSpace(city)) return null;
        var c = string.Join(' ', city.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(',', '.', ' ');
        if (c.Length == 0) return null;
        // „BERLIN“ → „Berlin“; запазваме смесения регистър както е
        if (c.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)))
            c = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(c.ToLowerInvariant());
        return c;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
