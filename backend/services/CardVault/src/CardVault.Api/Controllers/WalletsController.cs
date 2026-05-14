using CardVault.Api.Contracts;
using CardVault.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/wallets")]
[Authorize(Policy = "CanManageWallets")]
public class WalletsController : ControllerBase
{
    private readonly WalletService _service;

    public WalletsController(WalletService service)
    {
        _service = service;
    }

    [HttpPost("enrollments")]
    public async Task<ActionResult<WalletEnrollmentView>> Register([FromBody] RegisterWalletTokenRequest request, CancellationToken ct)
        => Ok(await _service.RegisterAsync(request, ct));

    [HttpPost("enrollments/{walletTokenId:guid}/activate")]
    public async Task<ActionResult<WalletTokenView>> Activate(Guid walletTokenId, [FromBody] ActivateWalletTokenRequest request, CancellationToken ct)
        => Ok(await _service.ActivateAsync(walletTokenId, request, ct));

    [HttpGet("cards/{cardId:guid}/tokens")]
    public async Task<ActionResult<IReadOnlyList<WalletTokenView>>> GetByCard(Guid cardId, CancellationToken ct)
        => Ok(await _service.GetByCardAsync(cardId, ct));

    [HttpPost("authorizations")]
    [Authorize(Policy = "CanOperateWalletPayments")]
    public async Task<ActionResult<WalletAuthorizationView>> Authorize([FromBody] AuthorizeWalletPaymentRequest request, CancellationToken ct)
        => Ok(await _service.AuthorizeAsync(request, ct));
}
