using MediatR;
using Microsoft.AspNetCore.Http;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Catalog;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;
using CardVault.Application.Services;
using CardVault.Application.Contracts;

namespace CardVault.Application.Features.Issuer.Commands;

public record CreateCustomerCommand(CreateCustomerRequest Request) : IRequest<IResult>;
public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, IResult>
{
    private readonly CustomerService _customers;

    public CreateCustomerCommandHandler(CustomerService customers)
    {
        _customers = customers;
    }

    public async Task<IResult> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var c = await _customers.CreateAsync(req.FullName, req.DocumentId, req.Email, req.Phone, req.DocumentType, req.Gender, req.BillingAddress, req.StatementAddress, req.ResidenceCity, req.StatementCity, req.CardDeliveryCity, cancellationToken);
        return Results.Created($"/api/issuer/customers/{c.Id}", c);
    }
}

public record IssueAccountCommand(CreateAccountRequest Request) : IRequest<IResult>;
public class IssueAccountCommandHandler : IRequestHandler<IssueAccountCommand, IResult>
{
    private readonly IssuerService _issuer;

    public IssueAccountCommandHandler(IssuerService issuer)
    {
        _issuer = issuer;
    }

    public async Task<IResult> Handle(IssueAccountCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var acc = await _issuer.CreateAccountAsync(req.CustomerId, req.AccountType, req.ProductCode, req.CreditLimit, cancellationToken);
        return Results.Created($"/api/issuer/accounts/{acc.Id}", acc);
    }
}

public record IssueCardCommand(IssueCardRequest Request) : IRequest<IResult>;
public class IssueCardCommandHandler : IRequestHandler<IssueCardCommand, IResult>
{
    private readonly IssuerService _issuer;

    public IssueCardCommandHandler(IssuerService issuer)
    {
        _issuer = issuer;
    }

    public async Task<IResult> Handle(IssueCardCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var card = await _issuer.IssueCardAsync(req.AccountId, req.Bin, req.Pan, req.ExpiryYyMm, cancellationToken);
        return Results.Created($"/api/issuer/cards/{card.Id}", new { card.Id, card.AccountId, card.Bin, card.PanToken, card.MaskedPan, card.ExpiryYyMm, card.Status, card.CreatedOn });
    }
}

public record ActivateCardCommand(Guid Id) : IRequest<IResult>;
public class ActivateCardCommandHandler : IRequestHandler<ActivateCardCommand, IResult>
{
    private readonly IssuerService _issuer;

    public ActivateCardCommandHandler(IssuerService issuer)
    {
        _issuer = issuer;
    }

    public async Task<IResult> Handle(ActivateCardCommand request, CancellationToken cancellationToken)
    {
        var card = await _issuer.ChangeStatusAsync(request.Id, CardStatus.Active, "activated", cancellationToken);
        return card is null ? Results.NotFound() : Results.Ok(new { card.Id, card.Status });
    }
}

public record BlockCardCommand(Guid Id, BlockCardRequest Request) : IRequest<IResult>;
public class BlockCardCommandHandler : IRequestHandler<BlockCardCommand, IResult>
{
    private readonly IssuerService _issuer;

    public BlockCardCommandHandler(IssuerService issuer)
    {
        _issuer = issuer;
    }

    public async Task<IResult> Handle(BlockCardCommand request, CancellationToken cancellationToken)
    {
        var (error, card) = await _issuer.BlockCardAsync(request.Id, request.Request.Reason, cancellationToken);
        return error switch
        {
            CardLifecycleError.NotFound => Results.NotFound(),
            _ => Results.Ok(new { card!.Id, card.Status })
        };
    }
}

public record UpsertCreditPolicyCommand(CreditPolicyEntity Policy) : IRequest<IResult>;
public class UpsertCreditPolicyCommandHandler : IRequestHandler<UpsertCreditPolicyCommand, IResult>
{
    private readonly CreditPolicyService _policies;

    public UpsertCreditPolicyCommandHandler(CreditPolicyService policies)
    {
        _policies = policies;
    }

    public async Task<IResult> Handle(UpsertCreditPolicyCommand request, CancellationToken cancellationToken)
    {
        var saved = await _policies.UpsertAsync(request.Policy, cancellationToken);
        return Results.Ok(saved);
    }
}

public record SetPinCommand(Guid CardId, SetPinRequest Request) : IRequest<IResult>;
public class SetPinCommandHandler : IRequestHandler<SetPinCommand, IResult>
{
    private readonly PinService _pin;

    public SetPinCommandHandler(PinService pin)
    {
        _pin = pin;
    }

    public async Task<IResult> Handle(SetPinCommand request, CancellationToken cancellationToken)
    {
        await _pin.SetPinAsync(request.CardId, request.Request.Pin, cancellationToken);
        return Results.NoContent();
    }
}
public record UpdateAccountLimitsCommand(AccountLimitEntity Limits) : IRequest<IResult>;
public class UpdateAccountLimitsCommandHandler : IRequestHandler<UpdateAccountLimitsCommand, IResult>
{
    private readonly CardVaultDbContext _db;

    public UpdateAccountLimitsCommandHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(UpdateAccountLimitsCommand request, CancellationToken cancellationToken)
    {
        var existing = await _db.AccountLimits.FirstOrDefaultAsync(x => x.AccountId == request.Limits.AccountId, cancellationToken);
        if (existing == null)
        {
            _db.AccountLimits.Add(request.Limits);
        }
        else
        {
            existing.DailyAtmLimit = request.Limits.DailyAtmLimit;
            existing.DailyPosLimit = request.Limits.DailyPosLimit;
            existing.DailyEcommerceLimit = request.Limits.DailyEcommerceLimit;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Results.Ok();
    }
}

// ── Card Lifecycle: Unblock ───────────────────────────────────────────────────

public record UnblockCardCommand(Guid Id) : IRequest<IResult>;
public class UnblockCardCommandHandler : IRequestHandler<UnblockCardCommand, IResult>
{
    private readonly IssuerService _issuer;

    public UnblockCardCommandHandler(IssuerService issuer) => _issuer = issuer;

    public async Task<IResult> Handle(UnblockCardCommand request, CancellationToken cancellationToken)
    {
        var (error, _) = await _issuer.UnblockCardAsync(request.Id, cancellationToken);
        return error switch
        {
            CardLifecycleError.NotFound     => Results.NotFound(),
            CardLifecycleError.InvalidStatus => Results.Conflict(new { message = "Card is not in Blocked status." }),
            _                               => Results.NoContent()
        };
    }
}

// ── Card Lifecycle: Cancel ───────────────────────────────────────────────────

public record CancelCardCommand(Guid Id, CancelCardRequest Request) : IRequest<IResult>;
public class CancelCardCommandHandler : IRequestHandler<CancelCardCommand, IResult>
{
    private readonly IssuerService _issuer;

    public CancelCardCommandHandler(IssuerService issuer) => _issuer = issuer;

    public async Task<IResult> Handle(CancelCardCommand request, CancellationToken cancellationToken)
    {
        var (error, _) = await _issuer.CancelCardAsync(request.Id, request.Request.Reason, cancellationToken);
        return error switch
        {
            CardLifecycleError.NotFound     => Results.NotFound(),
            CardLifecycleError.InvalidStatus => Results.Conflict(new { message = "Card is already cancelled." }),
            _                               => Results.NoContent()
        };
    }
}

// ── Card Lifecycle: Replace ───────────────────────────────────────────────────

public record ReplaceCardCommand(Guid Id, ReplaceCardRequest Request) : IRequest<IResult>;
public class ReplaceCardCommandHandler : IRequestHandler<ReplaceCardCommand, IResult>
{
    private readonly IssuerService _issuer;

    public ReplaceCardCommandHandler(IssuerService issuer) => _issuer = issuer;

    public async Task<IResult> Handle(ReplaceCardCommand request, CancellationToken cancellationToken)
    {
        var (error, newCard) = await _issuer.ReplaceCardAsync(request.Id, request.Request.Reason, cancellationToken);
        return error switch
        {
            CardLifecycleError.NotFound      => Results.NotFound(),
            CardLifecycleError.InvalidStatus => Results.Conflict(new { message = "Cancelled cards cannot be replaced." }),
            _                                => Results.Created(
                $"/api/issuer/cards/{newCard!.Id}",
                new { newCardId = newCard!.Id })
        };
    }
}
