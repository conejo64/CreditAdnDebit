using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Switch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/risk/rules")]
[Authorize(Policy = "CanManageRisk")]
public sealed class AntifraudController : ControllerBase
{
    private readonly CardVaultDbContext _db;

    public AntifraudController(CardVaultDbContext db) => _db = db;

    [HttpGet]
    public async Task<IResult> GetRules(CancellationToken ct)
    {
        var rules = await _db.AntifraudRules.AsNoTracking().OrderByDescending(x => x.CreatedOn).ToListAsync(ct);
        return Results.Ok(rules);
    }

    [HttpPost]
    public async Task<IResult> UpsertRule([FromBody] AntifraudRuleRequest req, CancellationToken ct)
    {
        var rule = await _db.AntifraudRules.FirstOrDefaultAsync(x => x.Id == req.Id, ct);
        if (rule is null)
        {
            rule = new AntifraudRuleEntity { Id = Guid.NewGuid(), CreatedOn = DateTimeOffset.UtcNow };
            _db.AntifraudRules.Add(rule);
        }

        rule.Type = req.Type;
        rule.TargetValue = req.TargetValue;
        rule.RiskScore = req.RiskScore;
        rule.IsEnabled = req.IsEnabled;
        rule.Description = req.Description;

        await _db.SaveChangesAsync(ct);
        return Results.Ok(rule);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IResult> DeleteRule(Guid id, CancellationToken ct)
    {
        var rule = await _db.AntifraudRules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (rule is null) return Results.NotFound();

        _db.AntifraudRules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

public record AntifraudRuleRequest(Guid? Id, AntifraudRuleType Type, string TargetValue, decimal RiskScore, bool IsEnabled, string? Description);
