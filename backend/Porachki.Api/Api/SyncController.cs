using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Porachki.Api.Data;
using Porachki.Api.Domain;
using Porachki.Api.Sync;

namespace Porachki.Api.Api;

[ApiController]
[Route("api/sync")]
public class SyncController(AppDbContext db, TenderQuery query, SyncWorker worker, IOptions<SyncOptions> options) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<IReadOnlyList<SyncStatus>>> Status(CancellationToken ct)
    {
        var result = new List<SyncStatus>();
        foreach (var source in new[] { TenderSource.Bg, TenderSource.Eu })
        {
            var last = await db.SyncRuns.AsNoTracking().Where(r => r.Source == source)
                .OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct);
            var lastOk = await db.SyncRuns.AsNoTracking().Where(r => r.Source == source && r.Success)
                .OrderByDescending(r => r.StartedAt).Select(r => r.FinishedAt).FirstOrDefaultAsync(ct);
            var count = await query.Active(source).CountAsync(ct);
            var running = last is not null && last.FinishedAt is null;
            result.Add(new SyncStatus(source == TenderSource.Bg ? "bg" : "eu", last?.StartedAt, lastOk, running ? null : last?.Success, last?.Message, count, running));
        }
        return result;
    }

    /// <summary>Ръчно пускане на синхронизация. Изисква заглавка X-Admin-Key.</summary>
    [HttpPost("{source}/run")]
    public IActionResult Run(string source, [FromHeader(Name = "X-Admin-Key")] string? key)
    {
        var expected = options.Value.AdminApiKey;
        if (string.IsNullOrEmpty(expected) || key != expected) return Unauthorized();
        if (!TendersController.TryParse(source, out var s)) return NotFound();
        worker.Trigger(s);
        return Accepted();
    }
}
