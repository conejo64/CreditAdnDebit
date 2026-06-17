using CardVault.Application.Contracts;
using CardVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/admin/open-banking")]
[Authorize(Policy = "CanManageUsersRoles")]
public class OpenBankingAdminController : ControllerBase
{
    private readonly OpenBankingService _service;

    public OpenBankingAdminController(OpenBankingService service)
    {
        _service = service;
    }

    [HttpGet("clients")]
    public async Task<ActionResult<IReadOnlyList<OpenBankingClientResponse>>> GetClients(CancellationToken ct)
        => Ok(await _service.GetClientsAsync(ct));

    [HttpPost("clients")]
    public async Task<ActionResult<OpenBankingClientResponse>> CreateClient([FromBody] CreateOpenBankingClientRequest request, CancellationToken ct)
    {
        var created = await _service.CreateClientAsync(request, ct);
        return Created($"/api/admin/open-banking/clients/{created.ClientId}", created);
    }

    [HttpPost("clients/{clientId}/accounts/{accountId:guid}")]
    public async Task<ActionResult<OpenBankingClientResponse>> GrantAccountAccess(string clientId, Guid accountId, CancellationToken ct)
    {
        var updated = await _service.GrantAccountAccessAsync(clientId, accountId, ct);
        return updated is null ? NotFound(new { message = "Open Banking client not found." }) : Ok(updated);
    }
}
