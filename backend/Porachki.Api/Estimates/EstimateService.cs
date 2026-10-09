using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Porachki.Api.Data;
using Porachki.Api.Domain;

namespace Porachki.Api.Estimates;

/// <summary>Изпълнява една оценка: документи от ЦАИС ЕОП → текст → Claude → резултат в базата.</summary>
public class EstimateService(
    AppDbContext db,
    EopDocumentClient eop,
    DocumentTextExtractor extractor,
    ClaudeClient claude,
    IOptions<EstimateOptions> options,
    ILogger<EstimateService> log)
{
    private readonly EstimateOptions _opt = options.Value;

    private record DocInfo(string Name, string Status, long SizeKb, int Chars, bool Included);

    /// <summary>Ключови думи за подреждане: най-важните документи влизат първи, ако текстът трябва да се съкрати.</summary>
    private static readonly (string Word, int Weight)[] Priority =
    [
        ("количеств", 100), ("сметк", 95), ("остойностен", 95), ("ксс", 95),
        ("техническа спецификация", 90), ("спецификац", 85), ("технически", 80), ("задание", 80),
        ("указани", 70), ("документаци", 65), ("обявлени", 60), ("решени", 40),
        ("проект на договор", 60), ("договор", 55), ("методика", 50), ("ценово предложение", 45), ("образ", 20),
    ];

    public async Task RunAsync(long estimateId, CancellationToken ct)
    {
        var estimate = await db.TenderEstimates.FirstAsync(e => e.Id == estimateId, ct);
        var tender = await db.Tenders.AsNoTracking().FirstOrDefaultAsync(t => t.Id == estimate.TenderId, ct);
        estimate.Status = EstimateStatus.Running;
        estimate.Model = _opt.Model;
        await SaveStage(estimate, "Подготовка", ct);

        try
        {
            if (tender is null) throw new InvalidOperationException("Поръчката вече не съществува в базата.");
            if (!int.TryParse(tender.NoticeNumber, out var eopId))
                throw new InvalidOperationException("Поръчката няма номер в ЦАИС ЕОП, по който да се намерят документите.");

            // 1) Документи
            await SaveStage(estimate, "Зареждане на списъка с документи от ЦАИС ЕОП", ct);
            var main = await eop.GetTenderAsync(eopId, ct);
            var documents = new List<EopDocumentClient.EopDocument>(main.Documents);
            foreach (var childId in main.ChildTenderIds.Take(15))
            {
                try { documents.AddRange((await eop.GetTenderAsync(childId, ct)).Documents); }
                catch (Exception ex) { log.LogWarning(ex, "Обособена позиция {Id} не се зареди", childId); }
            }
            documents = documents.GroupBy(d => d.Id).Select(g => g.First()).ToList();

            var infos = new List<DocInfo>();
            var extracted = new List<ExtractedFile>();
            long total = 0;
            var maxFile = _opt.MaxFileMb * 1024L * 1024;
            var maxTotal = _opt.MaxTotalMb * 1024L * 1024;
            var files = documents.Where(d => !d.IsFolder).Take(_opt.MaxFiles).ToList();
            foreach (var folder in documents.Where(d => d.IsFolder)) infos.Add(new DocInfo(folder.Name, "пропусната папка", 0, 0, false));
            foreach (var skipped in documents.Where(d => !d.IsFolder).Skip(_opt.MaxFiles)) infos.Add(new DocInfo(skipped.Name, "пропуснат: над лимита на брой файлове", skipped.Size / 1024, 0, false));

            for (var i = 0; i < files.Count; i++)
            {
                var d = files[i];
                await SaveStage(estimate, $"Сваляне на документи ({i + 1}/{files.Count})", ct);
                if (total >= maxTotal) { infos.Add(new DocInfo(d.Name, "пропуснат: над общия лимит за размер", d.Size / 1024, 0, false)); continue; }
                byte[]? data;
                try { data = await eop.DownloadAsync(d, Math.Min(maxFile, maxTotal - total), ct); }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Документ {Name} не се свали", d.Name);
                    infos.Add(new DocInfo(d.Name, "грешка при сваляне", d.Size / 1024, 0, false));
                    continue;
                }
                if (data is null) { infos.Add(new DocInfo(d.Name, "пропуснат: твърде голям", d.Size / 1024, 0, false)); continue; }
                total += data.Length;
                extracted.AddRange(extractor.Extract(d.Name, data));
            }

            if (extracted.Count == 0 && string.IsNullOrWhiteSpace(main.Description))
                throw new InvalidOperationException("Не бяха намерени документи за поръчката. Възможно е документацията да е в папки или да е достъпна само след регистрация.");

            // 2) Съдържание за модела
            await SaveStage(estimate, "Подготовка на документите за анализ", ct);
            var content = BuildContent(tender, main, extracted, infos);

            // 3) Анализ
            await SaveStage(estimate, "Анализ с Claude (обикновено 1–3 минути)", ct);
            var result = await claude.CompleteAsync(EstimatePrompt.System(DateOnly.FromDateTime(DateTime.UtcNow)), content, EstimatePrompt.Schema(), ct);
            var json = ExtractJson(result.Text)
                ?? throw new InvalidOperationException(result.StopReason == "max_tokens"
                    ? "Отговорът на модела беше прекъснат (max_tokens). Увеличете Estimates:MaxOutputTokens."
                    : "Моделът не върна валиден JSON.");

            estimate.ResultJson = json;
            estimate.DocumentsJson = JsonSerializer.Serialize(infos, JsonOptions);
            estimate.InputTokens = result.InputTokens;
            estimate.OutputTokens = result.OutputTokens;
            estimate.CostUsd = Math.Round(result.InputTokens * _opt.PriceInputPerMTok / 1_000_000m + result.OutputTokens * _opt.PriceOutputPerMTok / 1_000_000m, 4);
            estimate.Status = EstimateStatus.Done;
            estimate.Stage = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Оценката {Id} се провали", estimateId);
            estimate.Status = EstimateStatus.Failed;
            estimate.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            estimate.Stage = null;
        }
        estimate.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private JsonArray BuildContent(Tender tender, EopDocumentClient.EopTender eopTender, List<ExtractedFile> files, List<DocInfo> infos)
    {
        var content = new JsonArray();
        var sb = new StringBuilder();
        sb.AppendLine("<поръчка>");
        sb.AppendLine($"Предмет: {eopTender.Name ?? tender.Title}");
        sb.AppendLine($"Възложител: {tender.BuyerName}");
        sb.AppendLine($"Вид: {Labels.ContractTypeLabel(tender.ContractType)}; процедура: {tender.ProcedureLabel}");
        sb.AppendLine($"CPV: {tender.CpvCodes}");
        sb.AppendLine($"Място: {tender.Municipality ?? tender.City}, обл. {tender.Region}");
        sb.AppendLine($"Краен срок за оферти: {tender.Deadline:dd.MM.yyyy HH:mm} (UTC)");
        var value = eopTender.EstimatedValue ?? tender.EstimatedValue;
        sb.AppendLine($"Прогнозна стойност (по данни на ЦАИС ЕОП): {(value is null ? "не е посочена" : $"{value:N2} {tender.Currency}")}");
        if (!string.IsNullOrWhiteSpace(eopTender.Description)) sb.AppendLine($"Описание:\n{eopTender.Description}");
        sb.AppendLine("</поръчка>");

        // Текстове по приоритет, до лимита
        var budget = _opt.MaxTextChars;
        var nativeLeft = _opt.MaxNativePdfs;
        var nativeMaxBytes = _opt.MaxNativePdfMb * 1024L * 1024;
        var ordered = files.OrderByDescending(f => Score(f.Name)).ThenBy(f => f.Text?.Length ?? 0).ToList();

        foreach (var f in ordered)
        {
            if (f.NativePdf is not null && nativeLeft > 0 && f.NativePdf.Length <= nativeMaxBytes)
            {
                nativeLeft--;
                content.Add(new JsonObject
                {
                    ["type"] = "document",
                    ["title"] = f.Name,
                    ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "application/pdf", ["data"] = Convert.ToBase64String(f.NativePdf) },
                });
                infos.Add(new DocInfo(f.Name, "сканиран PDF – подаден като изображение", f.NativePdf.Length / 1024, 0, true));
                continue;
            }
            if (f.Text is null)
            {
                infos.Add(new DocInfo(f.Name, f.NativePdf is not null ? "сканиран PDF – над лимита за изображения" : f.Status, 0, 0, false));
                continue;
            }
            if (budget <= 2000)
            {
                infos.Add(new DocInfo(f.Name, "пропуснат: над лимита за текст", 0, f.Text.Length, false));
                continue;
            }
            var text = f.Text.Length > budget ? f.Text[..budget] + "\n… (съкратено)" : f.Text;
            budget -= text.Length;
            sb.Append("\n<документ име=\"").Append(f.Name).Append("\">\n").Append(text).Append("\n</документ>\n");
            infos.Add(new DocInfo(f.Name, text.Length < f.Text.Length ? "включен частично" : f.Status, 0, text.Length, true));
        }

        sb.AppendLine("\nНаправи оценката по документацията по-горе.");
        content.Add(new JsonObject { ["type"] = "text", ["text"] = sb.ToString() });
        return content;
    }

    private static int Score(string name)
    {
        var n = name.ToLowerInvariant();
        var best = 0;
        foreach (var (word, weight) in Priority) if (n.Contains(word) && weight > best) best = weight;
        if (n.EndsWith(".xlsx") || n.EndsWith(".xls")) best += 10; // КСС често са в Excel
        return best;
    }

    /// <summary>Взема JSON обекта от отговора (премахва ```json огради и текст около него).</summary>
    private static string? ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        var candidate = text[start..(end + 1)];
        try
        {
            using var _ = JsonDocument.Parse(candidate);
            return candidate;
        }
        catch (JsonException) { return null; }
    }

    private async Task SaveStage(TenderEstimate e, string stage, CancellationToken ct)
    {
        e.Stage = stage;
        await db.SaveChangesAsync(ct);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
