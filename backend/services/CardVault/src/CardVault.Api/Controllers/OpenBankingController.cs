using CardVault.Api.Contracts;
using CardVault.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/open-banking")]
public class OpenBankingController : ControllerBase
{
    private readonly OpenBankingService _service;

    public OpenBankingController(OpenBankingService service)
    {
        _service = service;
    }

    [HttpPost("oauth/token")]
    [AllowAnonymous]
    public async Task<IResult> Token([FromForm] OpenBankingTokenRequest request, CancellationToken ct)
    {
        var token = await _service.IssueTokenAsync(request, HttpContext.TraceIdentifier, ct);
        return token is null
            ? Results.Json(new { message = "Unauthorized" }, statusCode: 401)
            : Results.Ok(token);
    }

    [HttpGet("accounts/{accountId:guid}/balance")]
    [Authorize(Policy = "CanReadOpenBankingBalances")]
    public async Task<IResult> Balance(Guid accountId, CancellationToken ct)
    {
        var result = await _service.GetBalanceAsync(User, accountId, HttpContext.TraceIdentifier, ct);
        return result is null ? Results.Forbid() : Results.Ok(result);
    }

    [HttpGet("accounts/{accountId:guid}/transactions")]
    [Authorize(Policy = "CanReadOpenBankingTransactions")]
    public async Task<IResult> Transactions(Guid accountId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int take, CancellationToken ct)
    {
        var result = await _service.GetTransactionsAsync(User, accountId, from, to, take, HttpContext.TraceIdentifier, ct);
        return result is null ? Results.Forbid() : Results.Ok(result);
    }
}
