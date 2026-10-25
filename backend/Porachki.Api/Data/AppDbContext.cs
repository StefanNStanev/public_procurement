using Microsoft.EntityFrameworkCore;
using Porachki.Api.Domain;

namespace Porachki.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Tender> Tenders => Set<Tender>();
    public DbSet<CpvCode> CpvCodes => Set<CpvCode>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<TenderEstimate> TenderEstimates => Set<TenderEstimate>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_ai_ci");

        b.Entity<Tender>(e =>
        {
            e.ToTable("tenders");
            e.Property(x => x.Source).HasConversion<byte>();
            e.Property(x => x.ExternalId).HasMaxLength(120).IsRequired();
            e.Property(x => x.NoticeNumber).HasMaxLength(60);
            e.Property(x => x.Title).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Description).HasColumnType("mediumtext");
            e.Property(x => x.BuyerName).HasMaxLength(500);
            e.Property(x => x.ContractType).HasMaxLength(20);
            e.Property(x => x.ProcedureKey).HasMaxLength(120);
            e.Property(x => x.ProcedureLabel).HasMaxLength(250);
            e.Property(x => x.Arrangement).HasMaxLength(20);
            e.Property(x => x.MainCpv).HasMaxLength(10);
            e.Property(x => x.CpvDivision).HasMaxLength(2);
            e.Property(x => x.CpvCodes).HasMaxLength(2000);
            e.Property(x => x.CountryCode).HasMaxLength(2);
            e.Property(x => x.Region).HasMaxLength(120);
            e.Property(x => x.Municipality).HasMaxLength(120);
            e.Property(x => x.City).HasMaxLength(200);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.EstimatedValue).HasPrecision(18, 2);
            e.Property(x => x.ValueEur).HasPrecision(18, 2);
            e.Property(x => x.Status).HasMaxLength(30);
            e.Property(x => x.Url).HasMaxLength(500);

            e.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.Source, x.Deadline });
            e.HasIndex(x => new { x.Source, x.PublishedAt });
            e.HasIndex(x => new { x.Source, x.ContractType });
            e.HasIndex(x => new { x.Source, x.ProcedureKey });
            e.HasIndex(x => new { x.Source, x.Arrangement });
            e.HasIndex(x => new { x.Source, x.CpvDivision, x.MainCpv });
            e.HasIndex(x => new { x.Source, x.Municipality });
            e.HasIndex(x => new { x.Source, x.CountryCode, x.City });
        });

        b.Entity<CpvCode>(e =>
        {
            e.ToTable("cpv_codes");
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(10);
            e.Property(x => x.NameBg).HasMaxLength(500);
        });

        b.Entity<SyncRun>(e =>
        {
            e.ToTable("sync_runs");
            e.Property(x => x.Source).HasConversion<byte>();
            e.Property(x => x.Message).HasMaxLength(2000);
            e.HasIndex(x => new { x.Source, x.StartedAt });
        });

        // При промяна тук обновете и SQL-а в SchemaUpgrader (за бази, създадени преди тази таблица).
        b.Entity<TenderEstimate>(e =>
        {
            e.ToTable("tender_estimates");
            e.Property(x => x.Status).HasConversion<byte>();
            e.Property(x => x.Kind).HasConversion<byte>();
            e.Property(x => x.Stage).HasMaxLength(200);
            e.Property(x => x.Model).HasMaxLength(60);
            e.Property(x => x.CostUsd).HasPrecision(10, 4);
            e.Property(x => x.DocumentsJson).HasColumnType("mediumtext");
            e.Property(x => x.ResultJson).HasColumnType("mediumtext");
            e.Property(x => x.Error).HasMaxLength(2000);
            e.HasIndex(x => new { x.TenderId, x.Kind, x.CreatedAt });
        });
    }
}
