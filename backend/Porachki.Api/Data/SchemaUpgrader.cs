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
          KEY `IX_tender_estimates_TenderId_CreatedAt` (`TenderId`, `CreatedAt`)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
        """;

    public static async Task UpgradeAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(TenderEstimates, ct);
    }
}
