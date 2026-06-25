using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Catalog;
using CardVault.Application.Services;

namespace CardVault.Application.Features.Issuer.Queries;

public record GetCustomerQuery(Guid Id) : IRequest<IResult>;
public class GetCustomerQueryHandler : IRequestHandler<GetCustomerQuery, IResult>
{
    private readonly CustomerService _customers;

    public GetCustomerQueryHandler(CustomerService customers)
    {
        _customers = customers;
    }

    public async Task<IResult> Handle(GetCustomerQuery request, CancellationToken cancellationToken)
    {
        var c = await _customers.GetAsync(request.Id, cancellationToken);
        if (c is null) return Results.NotFound();

        return Results.Ok(new
        {
            c.Id,
            c.CustomerNumber,
            c.FullName,
            c.DocumentId,
            c.Email,
            c.Phone,
            c.DocumentType,
            c.Gender,
            c.BillingAddress,
            c.StatementAddress,
            c.ResidenceCity,
            c.StatementCity,
            c.CardDeliveryCity,
            c.CreatedOn,
            Accounts = c.Accounts.Select(a => new
            {
                a.Id,
                a.AccountNumber,
                a.AccountType,
                a.CurrencyCode,
                a.ProductCode,
                a.CreditLimit,
                a.AvailableLimit,
                a.LedgerBalance,
                a.Status,
                a.CreatedOn
            })
        });
    }
}

public record SearchCustomersQuery(string? Query, int Take) : IRequest<IResult>;
public class SearchCustomersQueryHandler : IRequestHandler<SearchCustomersQuery, IResult>
{
    private readonly CustomerService _customers;

    public SearchCustomersQueryHandler(CustomerService customers)
    {
        _customers = customers;
    }

    public async Task<IResult> Handle(SearchCustomersQuery request, CancellationToken cancellationToken)
    {
        int take = request.Take <= 0 ? 50 : Math.Min(request.Take, 200);
        var list = await _customers.SearchAsync(request.Query, take, cancellationToken);
        return Results.Ok(list);
    }
}

public record GetCardQuery(Guid Id) : IRequest<IResult>;
public class GetCardQueryHandler : IRequestHandler<GetCardQuery, IResult>
{
    private readonly IssuerService _issuer;

    public GetCardQueryHandler(IssuerService issuer)
    {
        _issuer = issuer;
    }

    public async Task<IResult> Handle(GetCardQuery request, CancellationToken cancellationToken)
    {
        var card = await _issuer.GetCardAsync(request.Id, cancellationToken);
        if (card is null)
            return Results.NotFound();
        return Results.Ok(new { card.Id, card.AccountId, card.Bin, card.PanToken, card.MaskedPan, card.ExpiryYyMm, card.Status, card.CreatedOn });
    }
}

public record GetCreditPolicyQuery(string ProductCode) : IRequest<IResult>;
public class GetCreditPolicyQueryHandler : IRequestHandler<GetCreditPolicyQuery, IResult>
{
    private readonly CreditPolicyService _policies;

    public GetCreditPolicyQueryHandler(CreditPolicyService policies)
    {
        _policies = policies;
    }

    public async Task<IResult> Handle(GetCreditPolicyQuery request, CancellationToken cancellationToken)
    {
        var p = await _policies.GetAsync(request.ProductCode, cancellationToken);
        return p is null ? Results.NotFound() : Results.Ok(p);
    }
}
public record GetAccountsQuery(string? Query, int Take) : IRequest<IResult>;
public class GetAccountsQueryHandler : IRequestHandler<GetAccountsQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public GetAccountsQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(GetAccountsQuery request, CancellationToken cancellationToken)
    {
        int take = request.Take <= 0 ? 50 : Math.Min(request.Take, 200);
        var q = _db.Accounts.Include(x => x.Customer).AsNoTracking();
        
        if (!string.IsNullOrEmpty(request.Query))
        {
            q = q.Where(x => x.AccountNumber.Contains(request.Query) || x.Customer.FullName.Contains(request.Query));
        }

        var list = await q.OrderByDescending(x => x.CreatedOn)
                          .Take(take)
                          .Select(x => new {
                              x.Id,
                              x.AccountNumber,
                              x.CustomerId,
                              CustomerName = x.Customer.FullName,
                              x.AccountType,
                              x.CurrencyCode,
                              x.AvailableLimit,
                              x.LedgerBalance,
                              x.Status,
                              x.CreatedOn
                          })
                          .ToListAsync(cancellationToken);

        return Results.Ok(list);
    }
}
public record GetAccountLimitsQuery(Guid AccountId) : IRequest<IResult>;
public class GetAccountLimitsQueryHandler : IRequestHandler<GetAccountLimitsQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public GetAccountLimitsQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(GetAccountLimitsQuery request, CancellationToken cancellationToken)
    {
        var limits = await _db.AccountLimits.FirstOrDefaultAsync(x => x.AccountId == request.AccountId, cancellationToken);
        if (limits == null)
        {
            // Return defaults if not exists
            return Results.Ok(new {
                AccountId = request.AccountId,
                DailyAtmLimit = 500m,
                DailyPosLimit = 2000m,
                DailyEcommerceLimit = 1000m,
                DailyAtmAuculated = 0m,
                DailyPosAccumulated = 0m,
                DailyEcommerceAccumulated = 0m
            });
        }
        return Results.Ok(limits);
    }
}
public record GetCardsQuery(string? Query, int Take) : IRequest<IResult>;
public class GetCardsQueryHandler : IRequestHandler<GetCardsQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public GetCardsQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(GetCardsQuery request, CancellationToken cancellationToken)
    {
        int take = request.Take <= 0 ? 50 : Math.Min(request.Take, 200);
        var q = _db.Cards.Include(x => x.Account).ThenInclude(x => x.Customer).AsNoTracking();

        if (!string.IsNullOrEmpty(request.Query))
        {
            q = q.Where(x => x.Last4.Contains(request.Query) || 
                            x.MaskedPan.Contains(request.Query) ||
                            x.Account.Customer.FullName.Contains(request.Query));
        }

        var list = await q.OrderByDescending(x => x.CreatedOn)
                          .Take(take)
                          .Select(x => new {
                              x.Id,
                              x.AccountId,
                              CustomerName = x.Account.Customer.FullName,
                              x.Bin,
                              x.PanToken,
                              x.MaskedPan,
                              x.ExpiryYyMm,
                              x.Status,
                              x.CreatedOn
                          })
                          .ToListAsync(cancellationToken);

        return Results.Ok(list);
    }
}
