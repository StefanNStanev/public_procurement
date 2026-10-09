using Microsoft.EntityFrameworkCore;
using Porachki.Api.Data;
using Porachki.Api.Domain;

namespace Porachki.Api.Sync;

/// <summary>Записва партиди от нормализирани поръчки (insert или merge на ненулевите полета).</summary>
public class TenderWriter(AppDbContext db)
{
    public const decimal BgnPerEur = 1.95583m;

    public async Task<int> UpsertAsync(IReadOnlyList<TenderUpsert> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return 0;
        var source = batch[0].Source;
        var now = DateTime.UtcNow;

        // Един запис на ExternalId – най-новият release/обявление
        var latest = batch
            .GroupBy(x => x.ExternalId)
            .Select(g => g.OrderBy(x => x.SourceUpdatedAt ?? DateTime.MinValue).Aggregate(Merge))
            .ToList();

        var ids = latest.Select(x => x.ExternalId).ToList();
        var existing = await db.Tenders
            .Where(t => t.Source == source && ids.Contains(t.ExternalId))
            .ToDictionaryAsync(t => t.ExternalId, ct);

        foreach (var u in latest)
        {
            if (!existing.TryGetValue(u.ExternalId, out var t))
            {
                if (u.Title is null) continue; // само award/contract без данни за поръчката – пропускаме
                t = new Tender { Source = source, ExternalId = u.ExternalId, FirstSeenAt = now };
                db.Tenders.Add(t);
            }
            else if (t.SourceUpdatedAt is { } known && u.SourceUpdatedAt is { } incoming && incoming < known)
            {
                continue; // по-стара версия
            }
            Apply(t, u, now);
        }

        await UpsertCpvNamesAsync(batch, ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return latest.Count;
    }

    /// <summary>По-новите ненулеви стойности печелят.</summary>
    private static TenderUpsert Merge(TenderUpsert older, TenderUpsert newer) => newer with
    {
        NoticeNumber = newer.NoticeNumber ?? older.NoticeNumber,
        Title = newer.Title ?? older.Title,
        Description = newer.Description ?? older.Description,
        BuyerName = newer.BuyerName ?? older.BuyerName,
        ContractType = newer.ContractType ?? older.ContractType,
        ProcedureKey = newer.ProcedureKey ?? older.ProcedureKey,
        ProcedureLabel = newer.ProcedureLabel ?? older.ProcedureLabel,
        CpvCodes = newer.CpvCodes.Count > 0 ? newer.CpvCodes : older.CpvCodes,
        CpvNames = newer.CpvNames.Count > 0 ? newer.CpvNames : older.CpvNames,
        CountryCode = newer.CountryCode ?? older.CountryCode,
        Region = newer.Region ?? older.Region,
        Municipality = newer.Municipality ?? older.Municipality,
        City = newer.City ?? older.City,
        PublishedAt = older.PublishedAt ?? newer.PublishedAt,
        Deadline = newer.Deadline ?? older.Deadline,
        EstimatedValue = newer.EstimatedValue ?? older.EstimatedValue,
        Currency = newer.Currency ?? older.Currency,
        Status = newer.Status ?? older.Status,
        Url = newer.Url ?? older.Url,
    };

    private static void Apply(Tender t, TenderUpsert u, DateTime now)
    {
        if (u.NoticeNumber is not null) t.NoticeNumber = Cut(u.NoticeNumber, 60);
        if (u.Title is not null) t.Title = Cut(u.Title, 1000)!;
        if (u.Description is not null) t.Description = u.Description;
        if (u.BuyerName is not null) t.BuyerName = Cut(u.BuyerName, 500);
        if (u.ContractType is not null) t.ContractType = u.ContractType;
        if (u.ProcedureKey is not null) { t.ProcedureKey = Cut(u.ProcedureKey, 120); t.ProcedureLabel = Cut(u.ProcedureLabel, 250); }
        if (u.CpvCodes.Count > 0)
        {
            t.MainCpv = u.CpvCodes[0];
            t.CpvDivision = u.CpvCodes[0][..2];
            t.CpvCodes = Cut(string.Join(',', u.CpvCodes), 2000);
        }
        if (u.CountryCode is not null) t.CountryCode = u.CountryCode;
        if (u.Region is not null) t.Region = Cut(u.Region, 120);
        if (u.Municipality is not null) t.Municipality = Cut(u.Municipality, 120);
        if (u.City is not null) t.City = Cut(u.City, 200);
        if (u.PublishedAt is not null) t.PublishedAt ??= u.PublishedAt;
        if (u.Deadline is not null) t.Deadline = u.Deadline;
        if (u.EstimatedValue is not null)
        {
            t.EstimatedValue = u.EstimatedValue;
            t.Currency = u.Currency;
            t.ValueEur = u.Currency switch
            {
                "EUR" => u.EstimatedValue,
                "BGN" => Math.Round(u.EstimatedValue.Value / BgnPerEur, 2),
                _ => null,
            };
        }
        if (u.Status is not null) t.Status = Cut(u.Status, 30);
        if (u.Url is not null) t.Url = Cut(u.Url, 500)!;
        if (u.SourceUpdatedAt is not null) t.SourceUpdatedAt = u.SourceUpdatedAt;
        t.LastUpdatedAt = now;
    }

    private async Task UpsertCpvNamesAsync(IReadOnlyList<TenderUpsert> batch, CancellationToken ct)
    {
        var names = new Dictionary<string, string>();
        foreach (var u in batch)
            foreach (var (code, name) in u.CpvNames)
                names.TryAdd(code, name);
        if (names.Count == 0) return;

        var codes = names.Keys.ToList();
        var known = await db.CpvCodes.Where(c => codes.Contains(c.Code)).ToDictionaryAsync(c => c.Code, ct);
        foreach (var (code, name) in names)
        {
            if (known.TryGetValue(code, out var c)) { c.NameBg ??= Cut(name, 500); }
            else db.CpvCodes.Add(new CpvCode { Code = code, NameBg = Cut(name, 500) });
        }
    }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
