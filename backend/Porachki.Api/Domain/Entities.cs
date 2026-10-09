namespace Porachki.Api.Domain;

public enum TenderSource
{
    Bg = 1, // ЦАИС ЕОП
    Eu = 2, // TED
}

/// <summary>Нормализирана обществена поръчка от който и да е източник.</summary>
public class Tender
{
    public long Id { get; set; }
    public TenderSource Source { get; set; }

    /// <summary>OCID при ЦАИС ЕОП; procedure-identifier (или publication-number) при TED.</summary>
    public string ExternalId { get; set; } = "";

    /// <summary>Номер на последното обявление (TED publication-number / ЦАИС ЕОП номер на поръчка).</summary>
    public string? NoticeNumber { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? BuyerName { get; set; }

    /// <summary>goods | services | works</summary>
    public string? ContractType { get; set; }

    /// <summary>Нормализиран ключ на вида процедура (за филтриране).</summary>
    public string? ProcedureKey { get; set; }
    public string? ProcedureLabel { get; set; }

    public string? MainCpv { get; set; }
    /// <summary>Първите 2 цифри на основния CPV код – „насока“.</summary>
    public string? CpvDivision { get; set; }
    /// <summary>Всички CPV кодове, разделени със запетая.</summary>
    public string? CpvCodes { get; set; }

    /// <summary>ISO 3166-1 alpha-2</summary>
    public string? CountryCode { get; set; }
    /// <summary>Област (BG) или NUTS код/регион (EU).</summary>
    public string? Region { get; set; }
    /// <summary>Само за BG.</summary>
    public string? Municipality { get; set; }
    public string? City { get; set; }

    public DateTime? PublishedAt { get; set; }
    public DateTime? Deadline { get; set; }

    public decimal? EstimatedValue { get; set; }
    public string? Currency { get; set; }
    /// <summary>Стойност в EUR (BGN по фиксирания курс; други валути – null).</summary>
    public decimal? ValueEur { get; set; }

    /// <summary>active | complete | cancelled | unsuccessful | planned … (OCDS) или null.</summary>
    public string? Status { get; set; }

    public string Url { get; set; } = "";

    public DateTime? SourceUpdatedAt { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}

/// <summary>Речник на CPV кодовете, попълван автоматично от данните (ЦАИС ЕОП дава описания на български).</summary>
public class CpvCode
{
    public string Code { get; set; } = "";
    public string? NameBg { get; set; }
}

public class SyncRun
{
    public long Id { get; set; }
    public TenderSource Source { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public bool Success { get; set; }
    public int Processed { get; set; }
    public string? Message { get; set; }
    /// <summary>ЦАИС ЕОП: последният ден с успешно обработени файлове. TED: максималната дата на публикуване.</summary>
    public DateOnly? CoveredUntil { get; set; }
}
