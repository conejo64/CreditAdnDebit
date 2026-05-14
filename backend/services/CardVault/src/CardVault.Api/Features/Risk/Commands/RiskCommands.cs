using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Api.Services;

namespace CardVault.Api.Features.Risk.Commands;

public record AssessOverlimitFeeCommand(Guid AccountId, DateOnly BusinessDate, DateTimeOffset? PostedOn) : IRequest<IResult>;
public class AssessOverlimitFeeCommandHandler : IRequestHandler<AssessOverlimitFeeCommand, IResult>
{
    private readonly FeeService _fees;

    public AssessOverlimitFeeCommandHandler(FeeService fees)
    {
        _fees = fees;
    }

    public async Task<IResult> Handle(AssessOverlimitFeeCommand request, CancellationToken cancellationToken)
    {
        var po = request.PostedOn ?? DateTimeOffset.UtcNow;
        var res = await _fees.AssessOverlimitAsync(request.AccountId, request.BusinessDate, po, cancellationToken);
        return res is null ? Results.NoContent() : Results.Ok(res);
    }
}

public record AssessAnnualFeeCommand(Guid AccountId, DateOnly BusinessDate, DateTimeOffset? PostedOn) : IRequest<IResult>;
public class AssessAnnualFeeCommandHandler : IRequestHandler<AssessAnnualFeeCommand, IResult>
{
    private readonly FeeService _fees;

    public AssessAnnualFeeCommandHandler(FeeService fees)
    {
        _fees = fees;
    }

    public async Task<IResult> Handle(AssessAnnualFeeCommand request, CancellationToken cancellationToken)
    {
        var po = request.PostedOn ?? DateTimeOffset.UtcNow;
        var res = await _fees.AssessAnnualAsync(request.AccountId, request.BusinessDate, po, cancellationToken);
        return res is null ? Results.NoContent() : Results.Ok(res);
    }
}

public record AssessCashAdvanceFeeCommand(Guid AccountId, decimal CashAmount, DateOnly BusinessDate, DateTimeOffset? PostedOn) : IRequest<IResult>;
public class AssessCashAdvanceFeeCommandHandler : IRequestHandler<AssessCashAdvanceFeeCommand, IResult>
{
    private readonly FeeService _fees;

    public AssessCashAdvanceFeeCommandHandler(FeeService fees)
    {
        _fees = fees;
    }

    public async Task<IResult> Handle(AssessCashAdvanceFeeCommand request, CancellationToken cancellationToken)
    {
        var po = request.PostedOn ?? DateTimeOffset.UtcNow;
        var res = await _fees.AssessCashAdvanceAsync(request.AccountId, request.BusinessDate, po, request.CashAmount, cancellationToken);
        return res is null ? Results.NoContent() : Results.Ok(res);
    }
}

public record AccrueInterestCommand(Guid AccountId, DateOnly From, DateOnly To) : IRequest<IResult>;
public class AccrueInterestCommandHandler : IRequestHandler<AccrueInterestCommand, IResult>
{
    private readonly DailyInterestAccrualService _accrual;

    public AccrueInterestCommandHandler(DailyInterestAccrualService accrual)
    {
        _accrual = accrual;
    }

    public async Task<IResult> Handle(AccrueInterestCommand request, CancellationToken cancellationToken)
    {
        var created = await _accrual.AccrueAsync(request.AccountId, request.From, request.To, cancellationToken);
        return Results.Ok(new { request.AccountId, request.From, request.To, created });
    }
}

public record UpsertMccRuleCommand(MccRuleUpsertRequest Request) : IRequest<IResult>;
public class UpsertMccRuleCommandHandler : IRequestHandler<UpsertMccRuleCommand, IResult>
{
    private readonly CardVaultDbContext _db;

    public UpsertMccRuleCommandHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(UpsertMccRuleCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var existing = await _db.MccRules.FirstOrDefaultAsync(x => x.Mcc == req.Mcc, cancellationToken);
        if (existing is null)
        {
            existing = new MccRuleEntity
            {
                Id = Guid.NewGuid(),
                Mcc = req.Mcc,
                IsBlocked = req.IsBlocked,
                PerTxnLimit = req.PerTxnLimit,
                Description = req.Description
            };
            _db.MccRules.Add(existing);
            await _db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/risk/mcc-rules/{existing.Mcc}", existing);
        }

        existing.IsBlocked = req.IsBlocked;
        existing.PerTxnLimit = req.PerTxnLimit;
        existing.Description = req.Description;
        await _db.SaveChangesAsync(cancellationToken);
        return Results.Ok(existing);
    }
}

public record UpsertVelocityRuleCommand(VelocityRuleUpsertRequest Request) : IRequest<IResult>;
public class UpsertVelocityRuleCommandHandler : IRequestHandler<UpsertVelocityRuleCommand, IResult>
{
    private readonly CardVaultDbContext _db;

    public UpsertVelocityRuleCommandHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(UpsertVelocityRuleCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var entity = new VelocityRuleEntity
        {
            Id = Guid.NewGuid(),
            ProductCode = req.ProductCode,
            WindowMinutes = req.WindowMinutes,
            MaxCount = req.MaxCount,
            MaxAmount = req.MaxAmount,
            Description = req.Description
        };
        _db.VelocityRules.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/risk/velocity-rules/{entity.Id}", entity);
    }
}

public record DeleteVelocityRuleCommand(Guid Id) : IRequest<IResult>;
public class DeleteVelocityRuleCommandHandler : IRequestHandler<DeleteVelocityRuleCommand, IResult>
{
    private readonly CardVaultDbContext _db;

    public DeleteVelocityRuleCommandHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(DeleteVelocityRuleCommand request, CancellationToken cancellationToken)
    {
        var existing = await _db.VelocityRules.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (existing is null)
            return Results.NotFound();
        _db.VelocityRules.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
}

public record DeleteMccRuleCommand(string Mcc) : IRequest<IResult>;
public class DeleteMccRuleCommandHandler : IRequestHandler<DeleteMccRuleCommand, IResult>
{
    private readonly CardVaultDbContext _db;

    public DeleteMccRuleCommandHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(DeleteMccRuleCommand request, CancellationToken cancellationToken)
    {
        var existing = await _db.MccRules.FirstOrDefaultAsync(x => x.Mcc == request.Mcc, cancellationToken);
        if (existing is null)
            return Results.NotFound();
        _db.MccRules.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
}
