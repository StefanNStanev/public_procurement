using Microsoft.EntityFrameworkCore;

namespace Porachki.Api.Data;

/// <summary>
/// EnsureCreated() не добавя таблици към вече съществуваща база. Тук се създават идемпотентно
/// таблиците, добавени след първата версия. При преминаване към EF миграции този клас отпада.
/// </summary>
public static class SchemaUpgrader
{
    private const string TenderEstimates = """
        CREATE TABLE IF NOT EXISTS `tender_estimates` (
          `Id` bigint NOT NULL AUTO_INCREMENT,
          `TenderId` bigint NOT NULL,
          `Kind` tinyint unsigned NOT NULL DEFAULT 0,
          `Status` tinyint unsigned NOT NULL,
          `Stage` varchar(200) NULL,
          `CreatedAt` datetime(6) NOT NULL,
          `FinishedAt` datetime(6) NULL,
          `Model` varchar(60) NULL,
          `InputTokens` int NULL,
          `OutputTokens` int NULL,
          `CostUsd` decimal(10,4) NULL,
          `DocumentsJson` mediumtext NULL,
          `ResultJson` mediumtext NULL,
          `Error` varchar(2000) NULL,
          PRIMARY KEY (`Id`),
          KEY `IX_tender_estimates_TenderId_Kind_CreatedAt` (`TenderId`, `Kind`, `CreatedAt`)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
        """;

    public static async Task UpgradeAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(TenderEstimates, ct);

        // v3: колона Kind (вид анализ) за бази, създадени преди нея. Старите редове стават Cost (0).
        if (!await ColumnExistsAsync(db, "tender_estimates", "Kind", ct))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE `tender_estimates` ADD COLUMN `Kind` tinyint unsigned NOT NULL DEFAULT 0 AFTER `TenderId`", ct);
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX `IX_tender_estimates_TenderId_Kind_CreatedAt` ON `tender_estimates` (`TenderId`, `Kind`, `CreatedAt`)", ct);
        }
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column, CancellationToken ct)
    {
        var count = await db.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS `Value` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = {0} AND COLUMN_NAME = {1}",
                table, column)
            .SingleAsync(ct);
        return count > 0;
    }
}
