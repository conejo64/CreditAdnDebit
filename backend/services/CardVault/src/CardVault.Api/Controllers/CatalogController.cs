using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CardVault.Application.Features.Catalog.Commands;
using CardVault.Application.Features.Catalog.Queries;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/catalog")]
[Authorize]
public class CatalogController : ControllerBase
{
    private readonly IMediator _mediator;

    public CatalogController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("countries")]
    public async Task<IActionResult> GetCountries(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCountriesQuery(), ct);
        return Ok(result);
    }

    [HttpPost("countries")]
    [Authorize(Policy = "CanManageSwitchRoutes")]
    public async Task<IResult> CreateCountry([FromBody] CreateCountryRequestDto req, CancellationToken ct)
    {
        return await _mediator.Send(new CreateCountryCommand(req), ct);
    }

    [HttpGet("bins")]
    public async Task<IActionResult> GetBins(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetBinRangesQuery(), ct);
        return Ok(result);
    }

    [HttpPost("bins")]
    [Authorize(Policy = "CanManageSwitchRoutes")]
    public async Task<IResult> CreateBin([FromBody] CreateBinRangeRequestDto req, CancellationToken ct)
    {
        return await _mediator.Send(new CreateBinRangeCommand(req), ct);
    }

    [HttpGet("card-products")]
    public async Task<IActionResult> GetCardProducts(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCardProductsQuery(), ct);
        return Ok(result);
    }

    [HttpPost("card-products")]
    [Authorize(Policy = "CanManageSwitchRoutes")]
    public async Task<IResult> CreateCardProduct([FromBody] CreateCardProductRequestDto req, CancellationToken ct)
    {
        return await _mediator.Send(new CreateCardProductCommand(req), ct);
    }

    [HttpGet("document-types")]
    public IActionResult GetDocumentTypes()
    {
        return Ok(new[] { "CEDULA", "RUC", "PASAPORTE" });
    }

    [HttpGet("genders")]
    public IActionResult GetGenders()
    {
        return Ok(new[] { "MASCULINO", "FEMENINO", "OTRO", "NO_ESPECIFICADO" });
    }

    [HttpGet("cities")]
    public IActionResult GetCities()
    {
        return Ok(new[] { 
            "GUAYAQUIL", "QUITO", "CUENCA", "MANTA", "MACHALA", 
            "DURAN", "PORTOVIEJO", "AMBATO", "SANTO DOMINGO", "LOJA" 
        });
    }
}
