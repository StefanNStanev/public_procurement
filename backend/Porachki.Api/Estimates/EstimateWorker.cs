using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Porachki.Api.Data;
using Porachki.Api.Domain;

namespace Porachki.Api.Estimates;

/// <summary>Опашка за оценки – изпълнява ги една по една във фонов режим. Оценка се пуска само от бутона в интерфейса.</summary>
public class EstimateWorker(IServiceScopeFactory scopes, ILogger<EstimateWorker> log) : BackgroundService
{
    private readonly Channel<long> _queue = Channel.CreateUnbounded<long>();

    public void Enqueue(long estimateId) => _queue.Writer.TryWrite(estimateId);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Анализът е само по заявка: прекъснатите от рестарт НЕ се пускат автоматично отново
        // (всяко пускане струва пари), а се маркират като неуспешни – потребителят решава дали да повтори.
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var interrupted = await db.TenderEstimates
                .Where(e => e.Status == EstimateStatus.Pending || e.Status == EstimateStatus.Running)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(e => e.Status, EstimateStatus.Failed)
                    .SetProperty(e => e.Stage, (string?)null)
                    .SetProperty(e => e.FinishedAt, (DateTime?)DateTime.UtcNow)
                    .SetProperty(e => e.Error, "Анализът беше прекъснат от рестарт на сървъра. Натиснете „Опитай отново“."), ct);
            if (interrupted > 0) log.LogInformation("{Count} прекъснати оценки са маркирани като неуспешни", interrupted);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Не успях да проверя прекъснатите оценки");
        }

        await foreach (var id in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<EstimateService>().RunAsync(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Грешка при оценка {Id}", id); }
        }
    }
}
