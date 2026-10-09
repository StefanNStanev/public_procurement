using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Porachki.Api.Data;
using Porachki.Api.Domain;
using Porachki.Api.Estimates;

namespace Porachki.Api.Api;

public record EstimateDto(
    long Id,
    string Status,
    string? Stage,
    DateTime CreatedAt,
    DateTime? FinishedAt,
    string? Model,
    int? InputTokens,
    int? OutputTokens,
    decimal? CostUsd,
    JsonElement? Result,
    JsonElement? Documents,
    string? Error);

/// <summary>AI оценка на поръчка от ЦАИС ЕОП по прикачените ѝ документи.</summary>
[ApiController]
[Route("api/bg/tenders/{id:long}/estimate")]
public class EstimatesController(AppDbContext db, EstimateWorker worker, ClaudeClient claude, IOptions<EstimateOptions> options) : ControllerBase
{
    /// <summary>Последната оценка за поръчката (404, ако няма).</summary>
    [HttpGet]
    public async Task<ActionResult<EstimateDto>> Get(long id, CancellationToken ct)
    {
        var e = await Latest(id, ct);
        return e is null ? NotFound() : ToDto(e);
    }

    /// <summary>Стартира оценка. Връща съществуваща готова оценка, освен ако force=true.</summary>
    [HttpPost]
    public async Task<ActionResult<EstimateDto>> Start(long id, [FromQuery] bool force, CancellationToken ct)
    {
        if (!claude.IsConfigured)
            return Problem(statusCode: 400, title: "Липсва Anthropic API ключ. Задайте Estimates:AnthropicApiKey (виж README).");

        var tender = await db.Tenders.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.Source == TenderSource.Bg, ct);
        if (tender is null) return NotFound();

        var latest = await Latest(id, ct);
        if (latest is { Status: EstimateStatus.Pending or EstimateStatus.Running }) return ToDto(latest);
        if (latest is { Status: EstimateStatus.Done } && !force) return ToDto(latest);

        var since = DateTime.UtcNow.AddDays(-1);
        var today = await db.TenderEstimates.CountAsync(e => e.CreatedAt >= since, ct);
        if (today >= options.Value.MaxEstimatesPerDay)
            return Problem(statusCode: 429, title: $"Достигнат е дневният лимит от {options.Value.MaxEstimatesPerDay} оценки (Estimates:MaxEstimatesPerDay).");

        var estimate = new TenderEstimate { TenderId = id, Status = EstimateStatus.Pending, Stage = "В опашката", CreatedAt = DateTime.UtcNow };
        db.TenderEstimates.Add(estimate);
        await db.SaveChangesAsync(ct);
        worker.Enqueue(estimate.Id);
        return Accepted(ToDto(estimate));
    }

    private Task<TenderEstimate?> Latest(long tenderId, CancellationToken ct) =>
        db.TenderEstimates.AsNoTracking().Where(e => e.TenderId == tenderId).OrderByDescending(e => e.Id).FirstOrDefaultAsync(ct);

    private static EstimateDto ToDto(TenderEstimate e) => new(
        e.Id,
        e.Status.ToString().ToLowerInvariant(),
        e.Stage,
        e.CreatedAt,
        e.FinishedAt,
        e.Model,
        e.InputTokens,
        e.OutputTokens,
        e.CostUsd,
        Parse(e.ResultJson),
        Parse(e.DocumentsJson),
        e.Error);

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
