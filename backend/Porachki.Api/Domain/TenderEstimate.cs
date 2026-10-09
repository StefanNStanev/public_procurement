namespace Porachki.Api.Domain;

public enum EstimateStatus : byte
{
    Pending = 0,
    Running = 1,
    Done = 2,
    Failed = 3,
}

/// <summary>AI оценка на поръчка по документацията ѝ в ЦАИС ЕОП.</summary>
public class TenderEstimate
{
    public long Id { get; set; }
    public long TenderId { get; set; }
    public EstimateStatus Status { get; set; }
    /// <summary>Текущ етап, показван в интерфейса („Сваляне на документи 3/12“).</summary>
    public string? Stage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Model { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? CostUsd { get; set; }
    /// <summary>JSON: списък с обработените документи и статуса им.</summary>
    public string? DocumentsJson { get; set; }
    /// <summary>JSON: резултатът от модела (виж EstimatePrompt.Schema).</summary>
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
}
