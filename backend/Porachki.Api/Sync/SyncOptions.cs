namespace Porachki.Api.Sync;

public class SyncOptions
{
    public bool Enabled { get; set; } = true;
    public string AdminApiKey { get; set; } = "";
    /// <summary>Поръчки с изтекъл срок се изтриват след толкова дни.</summary>
    public int RetentionDays { get; set; } = 30;
    /// <summary>Поръчка без срок се счита за активна толкова дни след публикуване.</summary>
    public int NoDeadlineActiveDays { get; set; } = 45;
    public EopOptions Eop { get; set; } = new();
    public TedOptions Ted { get; set; } = new();
}

public class EopOptions
{
    public bool Enabled { get; set; } = true;
    public double IntervalHours { get; set; } = 6;
    public int BackfillDays { get; set; } = 60;
    public int RecheckDays { get; set; } = 3;
    /// <summary>
    /// Шаблон на дневната папка с отворени данни. {date} → yyyy-MM-dd.
    /// Може да сочи и директно към файл (.json/.zip/.gz).
    /// </summary>
    public string DailyFolderUrlTemplate { get; set; } = "https://storage.eop.bg/open-data-{date}/";
    public string TenderUrlTemplate { get; set; } = "https://app.eop.bg/today/{id}";
}

public class TedOptions
{
    public bool Enabled { get; set; } = true;
    public double IntervalHours { get; set; } = 2;
    public string SearchUrl { get; set; } = "https://api.ted.europa.eu/v3/notices/search";
    public string NoticeUrlTemplate { get; set; } = "https://ted.europa.eu/bg/notice/-/detail/{id}";
    public string Query { get; set; } = "";
    public int PageSize { get; set; } = 100;
    public int DelayMs { get; set; } = 1100;
    public int MaxPages { get; set; } = 2000;
    public int IncrementalOverlapDays { get; set; } = 2;
    public string[] Languages { get; set; } = ["bul", "eng"];
    public string[] Fields { get; set; } = [];
}
