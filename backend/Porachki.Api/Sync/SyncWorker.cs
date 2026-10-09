using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Porachki.Api.Data;
using Porachki.Api.Domain;

namespace Porachki.Api.Sync;

/// <summary>Фонова услуга: стартира синхронизациите по график и при ръчна заявка.</summary>
public class SyncWorker(IServiceScopeFactory scopes, IOptions<SyncOptions> options, ILogger<SyncWorker> log) : BackgroundService
{
    private readonly SyncOptions _opt = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentQueue<TenderSource> _manual = new();

    public bool IsRunning => _lock.CurrentCount == 0;

    public void Trigger(TenderSource source) => _manual.Enqueue(source);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opt.Enabled) { log.LogInformation("Синхронизацията е изключена (Sync:Enabled=false)"); return; }
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                while (_manual.TryDequeue(out var s)) await RunAsync(s, ct);

                if (_opt.Eop.Enabled && await IsDueAsync(TenderSource.Bg, _opt.Eop.IntervalHours, ct)) await RunAsync(TenderSource.Bg, ct);
                if (_opt.Ted.Enabled && await IsDueAsync(TenderSource.Eu, _opt.Ted.IntervalHours, ct)) await RunAsync(TenderSource.Eu, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Грешка в графика за синхронизация"); }

            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task RunAsync(TenderSource source, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SyncService>().RunAsync(source, ct);
        }
        finally { _lock.Release(); }
    }

    private async Task<bool> IsDueAsync(TenderSource source, double intervalHours, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var last = await db.SyncRuns.Where(r => r.Source == source)
            .OrderByDescending(r => r.StartedAt).Select(r => (DateTime?)r.StartedAt).FirstOrDefaultAsync(ct);
        return last is null || DateTime.UtcNow - last.Value >= TimeSpan.FromHours(intervalHours);
    }
}
