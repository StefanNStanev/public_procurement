using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Porachki.Api.Infrastructure;

namespace Porachki.Api.Estimates;

/// <summary>
/// Публичната JSON услуга, която използва сайтът app.eop.bg (NX1Service.svc).
/// Не е официално документирано API – методите са взети от отворения код на ЦАИС ЕОП
/// (github.com/governmentbg/CAIS-EOP, NX1/Services/INX1Service.cs).
/// </summary>
public class EopDocumentClient(HttpClient http, IOptions<EstimateOptions> options, ILogger<EopDocumentClient> log)
{
    private readonly EstimateOptions _opt = options.Value;

    public record EopDocument(int Id, string Name, long Size, bool IsFolder);

    public record EopTender(int TenderId, string? Name, string? Description, decimal? EstimatedValue, List<EopDocument> Documents, List<int> ChildTenderIds);

    public async Task<EopTender> GetTenderAsync(int tenderId, CancellationToken ct)
    {
        using var doc = await CallAsync("GetPublishedTenderDetails", new { tenderId, ianaTimeZone = "Europe/Sofia" }, ct);
        var d = Unwrap(doc.RootElement, "GetPublishedTenderDetails");

        var documents = new List<EopDocument>();
        foreach (var x in d.Prop("TenderDescriptionDocuments").Items())
        {
            var id = (int?)x.Prop("Id").Dec();
            if (id is null) continue;
            if (x.Prop("IsHidden").Str() == "true") continue;
            documents.Add(new EopDocument(
                id.Value,
                x.Prop("Name").Str() ?? $"документ-{id}",
                (long)(x.Prop("Size").Dec() ?? 0),
                x.Prop("IsFolder").Str() == "true"));
        }

        var children = d.Prop("ChildTenders").Items()
            .Select(c => (int?)(c.Prop("TenderId").Dec() ?? c.Prop("Id").Dec()))
            .OfType<int>()
            .Where(id => id != tenderId)
            .Distinct()
            .ToList();

        return new EopTender(tenderId, d.Prop("TenderName").Str(), d.Prop("TenderDescription").Str(),
            d.Prop("EstimatedValue").Dec(), documents, children);
    }

    /// <summary>Сваля документ чрез временен подписан адрес. Връща null, ако е по-голям от лимита.</summary>
    public async Task<byte[]?> DownloadAsync(EopDocument document, long maxBytes, CancellationToken ct)
    {
        using var doc = await CallAsync("GetSignedUrlByDocumentId", new { documentId = document.Id }, ct);
        var signed = Unwrap(doc.RootElement, "GetSignedUrlByDocumentId");
        var url = signed.Prop("Url").Str() ?? throw new InvalidOperationException("ЦАИС ЕОП не върна адрес за сваляне");

        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength is { } len && len > maxBytes) return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > maxBytes) return null;
        }
        return ms.ToArray();
    }

    private async Task<JsonDocument> CallAsync(string method, object body, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opt.EopServiceUrl.TrimEnd('/')}/{method}")
            {
                Content = JsonContent.Create(body),
            };
            req.Headers.TryAddWithoutValidation("Origin", _opt.EopAppUrl);
            req.Headers.TryAddWithoutValidation("Referer", _opt.EopAppUrl + "/");
            req.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var resp = await http.SendAsync(req, ct);
            var code = (int)resp.StatusCode;
            if (resp.IsSuccessStatusCode)
                return await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var text = await resp.Content.ReadAsStringAsync(ct);
            if (attempt < 3 && (code == 429 || code >= 500))
            {
                log.LogWarning("ЦАИС ЕОП {Method}: HTTP {Code}, нов опит", method, code);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                continue;
            }
            throw new HttpRequestException($"ЦАИС ЕОП {method}: HTTP {code} {(text.Length > 300 ? text[..300] : text)}");
        }
    }

    /// <summary>WCF понякога обвива резултата в {"d": …} или {"MethodResult": …}.</summary>
    private static JsonElement Unwrap(JsonElement root, string method)
    {
        if (root.Prop("d") is { } d) root = d;
        if (root.Prop(method + "Result") is { } r) root = r;
        return root;
    }
}
