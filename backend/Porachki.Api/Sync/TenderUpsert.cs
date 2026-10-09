using Porachki.Api.Domain;

namespace Porachki.Api.Sync;

/// <summary>
/// Нормализиран запис от източник. Null полетата НЕ презаписват съществуващи стойности
/// (напр. OCDS release „award“ често няма заглавие – обновява само статуса).
/// </summary>
public record TenderUpsert
{
    public required TenderSource Source { get; init; }
    public required string ExternalId { get; init; }
    public string? NoticeNumber { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? BuyerName { get; init; }
    public string? ContractType { get; init; }
    public string? ProcedureKey { get; init; }
    public string? ProcedureLabel { get; init; }
    public IReadOnlyList<string> CpvCodes { get; init; } = [];
    public IReadOnlyDictionary<string, string> CpvNames { get; init; } = new Dictionary<string, string>();
    public string? CountryCode { get; init; }
    public string? Region { get; init; }
    public string? Municipality { get; init; }
    public string? City { get; init; }
    public DateTime? PublishedAt { get; init; }
    public DateTime? Deadline { get; init; }
    public decimal? EstimatedValue { get; init; }
    public string? Currency { get; init; }
    public string? Status { get; init; }
    public string? Url { get; init; }
    public DateTime? SourceUpdatedAt { get; init; }
}
