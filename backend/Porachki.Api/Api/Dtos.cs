using Microsoft.AspNetCore.Mvc;

namespace Porachki.Api.Api;

/// <summary>Филтри, общи за списъка и фасетите. Масивите се подават като повторени параметри (?divisions=45&amp;divisions=72).</summary>
public class TenderFilter
{
    /// <summary>Свободен текст – търси в предмета, описанието, възложителя и номера.</summary>
    [FromQuery(Name = "q")] public string? Q { get; set; }
    /// <summary>Вид поръчка: goods | services | works</summary>
    [FromQuery(Name = "contractTypes")] public string[]? ContractTypes { get; set; }
    /// <summary>Вид процедура (ключове от фасета procedures).</summary>
    [FromQuery(Name = "procedures")] public string[]? Procedures { get; set; }
    /// <summary>Насока – CPV раздел (2 цифри).</summary>
    [FromQuery(Name = "divisions")] public string[]? Divisions { get; set; }
    /// <summary>Община (само България). „__none__“ = неопределена.</summary>
    [FromQuery(Name = "municipalities")] public string[]? Municipalities { get; set; }
    /// <summary>Държава ISO2 (само Европа).</summary>
    [FromQuery(Name = "countries")] public string[]? Countries { get; set; }
    /// <summary>Град във формат „DE|Berlin“ (само Европа).</summary>
    [FromQuery(Name = "cities")] public string[]? Cities { get; set; }
    [FromQuery(Name = "deadlineFrom")] public DateTime? DeadlineFrom { get; set; }
    [FromQuery(Name = "deadlineTo")] public DateTime? DeadlineTo { get; set; }
    /// <summary>Минимална прогнозна стойност в EUR.</summary>
    [FromQuery(Name = "minValue")] public decimal? MinValue { get; set; }
    [FromQuery(Name = "maxValue")] public decimal? MaxValue { get; set; }
}

public class TenderListQuery : TenderFilter
{
    /// <summary>deadline (по подразбиране) | published | value</summary>
    [FromQuery(Name = "sort")] public string? Sort { get; set; }
    [FromQuery(Name = "page")] public int Page { get; set; } = 1;
    [FromQuery(Name = "pageSize")] public int PageSize { get; set; } = 50;
}

public record TenderListItem(
    long Id,
    string Source,
    string? NoticeNumber,
    string Title,
    string? BuyerName,
    string? ContractType,
    string? ContractTypeLabel,
    string? ProcedureLabel,
    string? MainCpv,
    string? CpvName,
    string? Division,
    string? DivisionLabel,
    string? Municipality,
    string? Region,
    string? CountryCode,
    string? CountryName,
    string? City,
    DateTime? PublishedAt,
    DateTime? Deadline,
    decimal? EstimatedValue,
    string? Currency,
    decimal? ValueEur,
    string Url);

public record TenderDetail(TenderListItem Tender, string? Description, IReadOnlyList<CpvItem> CpvCodes);

public record CpvItem(string Code, string? Name);

public record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record FacetValue(string Key, string Label, int Count, string? Group = null);

public record Facets(
    IReadOnlyList<FacetValue> ContractTypes,
    IReadOnlyList<FacetValue> Procedures,
    IReadOnlyList<FacetValue> Divisions,
    IReadOnlyList<FacetValue> Municipalities,
    IReadOnlyList<FacetValue> Countries,
    IReadOnlyList<FacetValue> Cities,
    int Total);

public record SyncStatus(string Source, DateTime? LastRunAt, DateTime? LastSuccessAt, bool? LastRunSuccess, string? LastMessage, int ActiveCount, bool Running);
