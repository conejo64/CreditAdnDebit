using CardVault.Application.Contracts;
using CardVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/loyalty")]
[Authorize(Policy = "CanViewLoyalty")]
public class LoyaltyController : ControllerBase
{
    private readonly LoyaltyService _service;

    public LoyaltyController(LoyaltyService service)
    {
        _service = service;
    }

    [HttpGet("programs")]
    public async Task<ActionResult<IReadOnlyList<RewardProgramView>>> GetPrograms(CancellationToken ct)
        => Ok(await _service.GetProgramsAsync(ct));

    [HttpPost("programs")]
    [Authorize(Policy = "CanManageLoyalty")]
    public async Task<ActionResult<RewardProgramView>> UpsertProgram([FromBody] UpsertRewardProgramRequest request, CancellationToken ct)
        => Ok(await _service.UpsertProgramAsync(request, ct));

    [HttpGet("catalog")]
    public async Task<ActionResult<IReadOnlyList<RewardCatalogItemView>>> GetCatalog(CancellationToken ct)
        => Ok(await _service.GetCatalogAsync(ct));

    [HttpPost("catalog")]
    [Authorize(Policy = "CanManageLoyalty")]
    public async Task<ActionResult<RewardCatalogItemView>> UpsertCatalog([FromBody] UpsertRewardCatalogItemRequest request, CancellationToken ct)
        => Ok(await _service.UpsertCatalogItemAsync(request, ct));

    [HttpGet("accounts/{accountId:guid}/balance")]
    public async Task<ActionResult<LoyaltyBalanceView>> GetBalance(Guid accountId, CancellationToken ct)
        => Ok(await _service.GetBalanceAsync(accountId, ct));

    [HttpGet("accounts/{accountId:guid}/entries")]
    public async Task<ActionResult<IReadOnlyList<LoyaltyEntryView>>> GetEntries(Guid accountId, [FromQuery] int take, CancellationToken ct)
        => Ok(await _service.GetEntriesAsync(accountId, take, ct));

    [HttpPost("accounts/{accountId:guid}/redeem")]
    [Authorize(Policy = "CanManageLoyalty")]
    public async Task<ActionResult<LoyaltyBalanceView>> Redeem(Guid accountId, [FromBody] RedeemRewardRequest request, CancellationToken ct)
        => Ok(await _service.RedeemRewardAsync(accountId, request, ct));
}
