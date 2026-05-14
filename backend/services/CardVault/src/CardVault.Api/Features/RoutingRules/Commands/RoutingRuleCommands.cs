using MediatR;
using Microsoft.AspNetCore.Http;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Outbox;
using CardVault.Infrastructure.Persistence.Routing;

namespace CardVault.Api.Features.RoutingRules.Commands;

public record CreateRoutingRuleCommand(RoutingRuleEntity Rule) : IRequest<IResult>;

public class CreateRoutingRuleCommandHandler : IRequestHandler<CreateRoutingRuleCommand, IResult>
{
    private readonly CardVaultDbContext _db;

    public CreateRoutingRuleCommandHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(CreateRoutingRuleCommand request, CancellationToken cancellationToken)
    {
        var rule = request.Rule;
        rule.Id = Guid.NewGuid();
        rule.UpdatedOn = DateTimeOffset.UtcNow;
        _db.RoutingRules.Add(rule);
        
        // publish event via outbox
        var payload = System.Text.Json.JsonSerializer.Serialize(new 
        { 
            type = "cv.routing.updated", 
            ruleId = rule.Id, 
            priority = rule.Priority, 
            binStart = rule.BinStart, 
            binEnd = rule.BinEnd, 
            connectorId = rule.ConnectorId, 
            enabled = rule.Enabled, 
            updatedOn = rule.UpdatedOn 
        });
        
        _db.OutboxMessages.Add(new OutboxMessageEntity 
        { 
            Topic = "cv.routing.updated", 
            Key = rule.Id.ToString("N"), 
            PayloadJson = payload 
        });
        
        await _db.SaveChangesAsync(cancellationToken);
        
        return Results.Created($"/api/routing-rules/{rule.Id}", rule);
    }
}
