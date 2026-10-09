using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Porachki.Api.Data;
using Porachki.Api.Domain;
using Porachki.Api.Sync.Eop;
using Porachki.Api.Sync.Ted;

namespace Porachki.Api.Sync;

public class SyncService(
    AppDbContext db,
    TenderWriter writer,
    EopOcdsSource eop,
    TedSource ted,
    IOptions<SyncOptions> options,
    ILogger<SyncService> log)
{
    private readonly SyncOptions _opt = options.Value;

    public async Task RunAsync(TenderSource source, CancellationToken ct)
    {
        var run = new SyncRun { Source = source, StartedAt = DateTime.UtcNow };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(ct);
        var runId = run.Id;

        var processed = 0;
        DateOnly? covered = null;
        string? message = null;
        var success = false;
        try
        {
            async Task Sink(IReadOnlyList<TenderUpsert> b) => processed += await writer.UpsertAsync(b, ct);

            (covered, message) = source switch
            {
                TenderSource.Bg => await SyncEopAsync(Sink, ct),
                TenderSource.Eu => await SyncTedAsync(Sink, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(source)),
            };
            await CleanupAsync(source, ct);
            success = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            message = "Прекъсната";
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Синхронизацията на {Source} се провали", source);
            message = ex.Message;
        }
        finally
        {
            db.ChangeTracker.Clear();
            var r = await db.SyncRuns.FirstAsync(x => x.Id == runId, CancellationToken.None);
            r.FinishedAt = DateTime.UtcNow;
            r.Success = success;
            r.Processed = processed;
            r.CoveredUntil = covered;
            r.Message = message is { Length: > 2000 } ? message[..2000] : message;
            await db.SaveChangesAsync(CancellationToken.None);
            log.LogInformation("Синхронизация {Source}: {Processed} записа, успех={Success}", source, processed, success);
        }
    }

    private async Task<(DateOnly?, string?)> SyncEopAsync(Func<IReadOnlyList<TenderUpsert>, Task> sink, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastCovered = await LastCoveredAsync(TenderSource.Bg, ct);
        var from = lastCovered is { } lc
            ? lc.AddDays(-_opt.Eop.RecheckDays)
            : today.AddDays(-_opt.Eop.BackfillDays);
        var floor = today.AddDays(-_opt.Eop.BackfillDays);
        if (from < floor) from = floor;

        DateOnly? covered = lastCovered;
        var missing = new List<string>();
        for (var day = from; day <= today; day = day.AddDays(1))
        {
            var r = await eop.ReadDayAsync(day, sink, ct);
            if (r.Found) { if (covered is null || day > covered) covered = day; }
            else missing.Add(day.ToString("dd.MM"));
        }
        return (covered, missing.Count > 0 ? $"Няма файлове за: {string.Join(", ", missing.TakeLast(10))}" : null);
    }

    private async Task<(DateOnly?, string?)> SyncTedAsync(Func<IReadOnlyList<TenderUpsert>, Task> sink, CancellationToken ct)
    {
        var lastCovered = await LastCoveredAsync(TenderSource.Eu, ct);
        DateOnly? since = lastCovered?.AddDays(-_opt.Ted.IncrementalOverlapDays);
        var (count, maxPublished) = await ted.FetchAsync(since, sink, ct);
        var covered = maxPublished ?? lastCovered;
        if (lastCovered is { } lc && covered < lc) covered = lc;
        return (covered, since is null ? $"Пълно зареждане: {count}" : $"Инкрементално от {since:dd.MM.yyyy}: {count}");
    }

    private Task<DateOnly?> LastCoveredAsync(TenderSource source, CancellationToken ct) =>
        db.SyncRuns
            .Where(r => r.Source == source && r.Success && r.CoveredUntil != null)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => r.CoveredUntil)
            .FirstOrDefaultAsync(ct);

    private async Task CleanupAsync(TenderSource source, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_opt.RetentionDays);
        var deleted = await db.Tenders
            .Where(t => t.Source == source && t.Deadline != null && t.Deadline < cutoff)
            .ExecuteDeleteAsync(ct);
        if (deleted > 0) log.LogInformation("Изтрити {Count} изтекли поръчки ({Source})", deleted, source);
    }
}
