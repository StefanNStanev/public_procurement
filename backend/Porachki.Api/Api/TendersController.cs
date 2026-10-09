using Microsoft.AspNetCore.Mvc;
using Porachki.Api.Domain;

namespace Porachki.Api.Api;

/// <summary>Активни поръчки. {source}: bg (ЦАИС ЕОП) или eu (TED).</summary>
[ApiController]
[Route("api/{source}/tenders")]
public class TendersController(TenderQuery query) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Paged<TenderListItem>>> List(string source, [FromQuery] TenderListQuery q, CancellationToken ct)
    {
        if (!TryParse(source, out var s)) return NotFound();
        return await query.ListAsync(s, q, ct);
    }

    [HttpGet("facets")]
    public async Task<ActionResult<Facets>> GetFacets(string source, [FromQuery] TenderFilter f, CancellationToken ct)
    {
        if (!TryParse(source, out var s)) return NotFound();
        return await query.FacetsAsync(s, f, ct);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<TenderDetail>> Get(string source, long id, CancellationToken ct)
    {
        if (!TryParse(source, out var s)) return NotFound();
        var t = await query.GetAsync(s, id, ct);
        return t is null ? NotFound() : t;
    }

    internal static bool TryParse(string source, out TenderSource s)
    {
        s = source.ToLowerInvariant() switch { "bg" => TenderSource.Bg, "eu" => TenderSource.Eu, _ => 0 };
        return s != 0;
    }
}
