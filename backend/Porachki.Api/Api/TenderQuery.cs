using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Porachki.Api.Data;
using Porachki.Api.Domain;
using Porachki.Api.Sync;

namespace Porachki.Api.Api;

public enum Facet { None, ContractType, Procedure, Division, Municipality, Country, City }

public class TenderQuery(AppDbContext db, IOptions<SyncOptions> options)
{
    public const string NoneKey = "__none__";
    private readonly int _noDeadlineDays = options.Value.NoDeadlineActiveDays;

    /// <summary>Само активни поръчки: не са приключени/прекратени и срокът не е изтекъл.</summary>
    public IQueryable<Tender> Active(TenderSource source)
    {
        var now = DateTime.UtcNow;
        var publishedFloor = now.AddDays(-_noDeadlineDays);
        string[] closed = ["complete", "cancelled", "unsuccessful", "withdrawn"];
        return db.Tenders.AsNoTracking().Where(t =>
            t.Source == source
            && (t.Status == null || !closed.Contains(t.Status))
            && (t.Deadline >= now || (t.Deadline == null && t.PublishedAt >= publishedFloor)));
    }

    /// <summary>Прилага филтрите; <paramref name="exclude"/> пропуска филтъра на фасета, който се изчислява.</summary>
    public static IQueryable<Tender> Apply(IQueryable<Tender> q, TenderFilter f, Facet exclude = Facet.None)
    {
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            foreach (var word in f.Q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8))
            {
                var w = word;
                q = q.Where(t => t.Title.Contains(w)
                                 || (t.BuyerName != null && t.BuyerName.Contains(w))
                                 || (t.NoticeNumber != null && t.NoticeNumber.Contains(w))
                                 || (t.Description != null && t.Description.Contains(w)));
            }
        }

        if (Has(f.ContractTypes) && exclude != Facet.ContractType)
            q = q.Where(t => f.ContractTypes!.Contains(t.ContractType!));

        if (Has(f.Procedures) && exclude != Facet.Procedure)
            q = q.Where(t => f.Procedures!.Contains(t.ProcedureKey!));

        if (Has(f.Divisions) && exclude != Facet.Division)
            q = q.Where(t => f.Divisions!.Contains(t.CpvDivision!));

        if (Has(f.Municipalities) && exclude != Facet.Municipality)
        {
            var names = f.Municipalities!.Where(m => m != NoneKey).ToArray();
            var includeNone = f.Municipalities!.Contains(NoneKey);
            q = q.Where(t => names.Contains(t.Municipality!) || (includeNone && t.Municipality == null));
        }

        if (Has(f.Countries) && exclude is not (Facet.Country or Facet.City))
            q = q.Where(t => f.Countries!.Contains(t.CountryCode!));

        if (Has(f.Cities) && exclude != Facet.City)
        {
            // „DE|Berlin“ → комбиниран ключ, сравняван с CONCAT(CountryCode, '|', City)
            var keys = f.Cities!.ToArray();
            q = q.Where(t => keys.Contains(t.CountryCode + "|" + t.City));
        }

        if (f.DeadlineFrom is { } df) q = q.Where(t => t.Deadline >= df);
        if (f.DeadlineTo is { } dt) { var end = dt.Date.AddDays(1); q = q.Where(t => t.Deadline < end); }
        if (f.MinValue is { } min) q = q.Where(t => t.ValueEur >= min);
        if (f.MaxValue is { } max) q = q.Where(t => t.ValueEur <= max);

        return q;
    }

    public async Task<Paged<TenderListItem>> ListAsync(TenderSource source, TenderListQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize, 10, 200);
        var page = Math.Max(1, query.Page);
        var q = Apply(Active(source), query);

        var total = await q.CountAsync(ct);
        q = query.Sort switch
        {
            "published" => q.OrderByDescending(t => t.PublishedAt).ThenBy(t => t.Id),
            "value" => q.OrderByDescending(t => t.ValueEur).ThenBy(t => t.Id),
            _ => q.OrderBy(t => t.Deadline == null).ThenBy(t => t.Deadline).ThenBy(t => t.Id),
        };

        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var names = await CpvNamesAsync(rows.Select(r => r.MainCpv).OfType<string>(), ct);
        return new Paged<TenderListItem>(rows.Select(r => ToItem(r, names)).ToList(), total, page, pageSize);
    }

    public async Task<TenderDetail?> GetAsync(TenderSource source, long id, CancellationToken ct)
    {
        var t = await db.Tenders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Source == source, ct);
        if (t is null) return null;
        var codes = (t.CpvCodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var names = await CpvNamesAsync(codes, ct);
        return new TenderDetail(ToItem(t, names), t.Description, codes.Select(c => new CpvItem(c, names.GetValueOrDefault(c))).ToList());
    }

    public async Task<Facets> FacetsAsync(TenderSource source, TenderFilter f, CancellationToken ct)
    {
        var active = Active(source);
        IQueryable<Tender> For(Facet facet) => Apply(active, f, facet);

        var total = await Apply(active, f).CountAsync(ct);

        var contractTypes = (await For(Facet.ContractType).GroupBy(t => t.ContractType)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .Where(x => x.Key != null)
            .Select(x => new FacetValue(x.Key!, Labels.ContractTypeLabel(x.Key) ?? x.Key!, x.Count))
            .OrderBy(x => x.Label).ToList();

        var procedures = (await For(Facet.Procedure).GroupBy(t => new { t.ProcedureKey, t.ProcedureLabel })
                .Select(g => new { g.Key.ProcedureKey, g.Key.ProcedureLabel, Count = g.Count() }).ToListAsync(ct))
            .Where(x => x.ProcedureKey != null)
            .GroupBy(x => x.ProcedureKey!)
            .Select(g => new FacetValue(g.Key, g.Select(x => x.ProcedureLabel).FirstOrDefault(l => l != null) ?? g.Key, g.Sum(x => x.Count)))
            .OrderByDescending(x => x.Count).ToList();

        var divisions = (await For(Facet.Division).GroupBy(t => t.CpvDivision)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .Where(x => x.Key != null)
            .Select(x => new FacetValue(x.Key!, Labels.DivisionLabel(x.Key) ?? x.Key!, x.Count))
            .OrderBy(x => x.Key).ToList();

        List<FacetValue> municipalities = [], countries = [], cities = [];
        if (source == TenderSource.Bg)
        {
            municipalities = (await For(Facet.Municipality).GroupBy(t => new { t.Municipality, t.Region })
                    .Select(g => new { g.Key.Municipality, g.Key.Region, Count = g.Count() }).ToListAsync(ct))
                .GroupBy(x => x.Municipality)
                .Select(g => new { g.Key, Region = g.Where(x => x.Region != null).OrderByDescending(x => x.Count).Select(x => x.Region).FirstOrDefault(), Count = g.Sum(x => x.Count) })
                .Select(x => x.Key is null
                    ? new FacetValue(NoneKey, "Неопределена", x.Count, null)
                    : new FacetValue(x.Key, x.Key == "Столична" ? "Столична община" : x.Key, x.Count, x.Region is null ? null : $"обл. {x.Region}"))
                .OrderBy(x => x.Key == NoneKey).ThenBy(x => x.Label).ToList();
        }
        else
        {
            countries = (await For(Facet.Country).GroupBy(t => t.CountryCode)
                    .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
                .Where(x => x.Key != null)
                .Select(x => new FacetValue(x.Key!, Domain.Countries.NameOf(x.Key), x.Count))
                .OrderBy(x => x.Label).ToList();

            var cityLimit = Has(f.Countries) ? 400 : 150;
            cities = (await For(Facet.City).Where(t => t.City != null)
                    .GroupBy(t => new { t.CountryCode, t.City })
                    .Select(g => new { g.Key.CountryCode, g.Key.City, Count = g.Count() })
                    .OrderByDescending(x => x.Count).Take(cityLimit).ToListAsync(ct))
                .Select(x => new FacetValue($"{x.CountryCode}|{x.City}", x.City!, x.Count, Domain.Countries.NameOf(x.CountryCode)))
                .ToList();
        }

        return new Facets(contractTypes, procedures, divisions, municipalities, countries, cities, total);
    }

    private async Task<Dictionary<string, string?>> CpvNamesAsync(IEnumerable<string> codes, CancellationToken ct)
    {
        var list = codes.Distinct().ToList();
        if (list.Count == 0) return new();
        return await db.CpvCodes.AsNoTracking().Where(c => list.Contains(c.Code)).ToDictionaryAsync(c => c.Code, c => c.NameBg, ct);
    }

    private static TenderListItem ToItem(Tender t, IReadOnlyDictionary<string, string?> cpvNames) => new(
        t.Id,
        t.Source == TenderSource.Bg ? "bg" : "eu",
        t.NoticeNumber,
        t.Title,
        t.BuyerName,
        t.ContractType,
        Labels.ContractTypeLabel(t.ContractType),
        t.ProcedureLabel,
        t.MainCpv,
        t.MainCpv is null ? null : cpvNames.GetValueOrDefault(t.MainCpv),
        t.CpvDivision,
        Labels.DivisionLabel(t.CpvDivision),
        t.Municipality == "Столична" ? "Столична община" : t.Municipality,
        t.Region,
        t.CountryCode,
        Domain.Countries.NameOf(t.CountryCode),
        t.City,
        t.PublishedAt,
        t.Deadline,
        t.EstimatedValue,
        t.Currency,
        t.ValueEur,
        t.Url);

    private static bool Has(string[]? a) => a is { Length: > 0 };
}
