using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Api.Services;
using CardVault.Api.Controllers;

namespace CardVault.Api.Features.Billing.Commands;

public record GenerateStatementCommand(GenerateStatementRequest Request) : IRequest<IResult>;
public record DeferPurchaseCommand(DeferPurchaseRequest Request) : IRequest<IResult>;
public class GenerateStatementCommandHandler : IRequestHandler<GenerateStatementCommand, IResult>
{
    private readonly BillingService _billing;

    public GenerateStatementCommandHandler(BillingService billing)
    {
        _billing = billing;
    }

    public async Task<IResult> Handle(GenerateStatementCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var st = await _billing.GenerateStatementAsync(req.AccountId, req.CycleStart, req.CycleEnd, req.StatementDate, req.DueDate, cancellationToken);
        return Results.Created($"/api/billing/statements/{st.Id}", st);
    }
}

public class DeferPurchaseCommandHandler : IRequestHandler<DeferPurchaseCommand, IResult>
{
    private readonly InstallmentService _installments;

    public DeferPurchaseCommandHandler(InstallmentService installments) => _installments = installments;

    public async Task<IResult> Handle(DeferPurchaseCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var plan = await _installments.DeferPurchaseAsync(req.AccountId, req.LedgerEntryId, req.Installments, req.Apr, ct);
        return Results.Ok(plan);
    }
}

public record ApplyPaymentCommand(Guid Id, ApplyPaymentRequest Request) : IRequest<IResult>;
public class ApplyPaymentCommandHandler : IRequestHandler<ApplyPaymentCommand, IResult>
{
    private readonly BillingService _billing;
    private readonly PaymentAllocatorService _allocator;

    public ApplyPaymentCommandHandler(BillingService billing, PaymentAllocatorService allocator)
    {
        _billing = billing;
        _allocator = allocator;
    }

    public async Task<IResult> Handle(ApplyPaymentCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var postedOn = req.PostedOn ?? DateTimeOffset.UtcNow;
        var st = await _billing.ApplyStatementPaymentAsync(request.Id, req.Amount, postedOn, cancellationToken);
        var alloc = await _allocator.AllocateAsync(request.Id, req.Amount, cancellationToken);
        return Results.Ok(new { statement = st, allocation = new { toInterest = alloc.toInterest, toFees = alloc.toFees, toPrincipal = alloc.toPrincipal } });
    }
}

public record ApplyLateFeeCommand(Guid Id, bool Force) : IRequest<IResult>;
public class ApplyLateFeeCommandHandler : IRequestHandler<ApplyLateFeeCommand, IResult>
{
    private readonly BillingService _billing;

    public ApplyLateFeeCommandHandler(BillingService billing)
    {
        _billing = billing;
    }

    public async Task<IResult> Handle(ApplyLateFeeCommand request, CancellationToken cancellationToken)
    {
        var st = await _billing.ApplyLateFeeIfNeededAsync(request.Id, request.Force, cancellationToken);
        return st is null ? Results.NotFound() : Results.Ok(st);
    }
}

public record RunLateFeesCommand(bool Force) : IRequest<IResult>;
public class RunLateFeesCommandHandler : IRequestHandler<RunLateFeesCommand, IResult>
{
    private readonly BillingMaintenanceService _maint;

    public RunLateFeesCommandHandler(BillingMaintenanceService maint)
    {
        _maint = maint;
    }

    public async Task<IResult> Handle(RunLateFeesCommand request, CancellationToken cancellationToken)
    {
        var applied = await _maint.ApplyLateFeesForPastDueAsync(request.Force, cancellationToken);
        return Results.Ok(new { applied, request.Force });
    }
}

public record RecalculateStatementCommand(Guid Id) : IRequest<IResult>;
public class RecalculateStatementCommandHandler : IRequestHandler<RecalculateStatementCommand, IResult>
{
    private readonly MinimumPaymentService _minpay;

    public RecalculateStatementCommandHandler(MinimumPaymentService minpay)
    {
        _minpay = minpay;
    }

    public async Task<IResult> Handle(RecalculateStatementCommand request, CancellationToken cancellationToken)
    {
        var st = await _minpay.RecalculateAsync(request.Id, cancellationToken);
        return Results.Ok(st);
    }
}

public record UpsertMinimumPaymentPolicyCommand(MinimumPaymentPolicyUpsert Request) : IRequest<IResult>;
public class UpsertMinimumPaymentPolicyCommandHandler : IRequestHandler<UpsertMinimumPaymentPolicyCommand, IResult>
{
    private readonly CardVaultDbContext _db;

    public UpsertMinimumPaymentPolicyCommandHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(UpsertMinimumPaymentPolicyCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var existing = await _db.MinimumPaymentPolicies.FirstOrDefaultAsync(x => x.Code == req.Code, cancellationToken);
        if (existing is null)
        {
            existing = new MinimumPaymentPolicyEntity
            {
                Id = Guid.NewGuid(),
                Code = req.Code,
                IsDefault = req.IsDefault,
                FloorAmount = req.FloorAmount,
                PrincipalPercent = req.PrincipalPercent,
                CeilingAmount = req.CeilingAmount,
                IncludeInterest = req.IncludeInterest,
                IncludeFees = req.IncludeFees,
                CreatedOn = DateTimeOffset.UtcNow
            };
            _db.MinimumPaymentPolicies.Add(existing);
        }
        else
        {
            existing.IsDefault = req.IsDefault;
            existing.FloorAmount = req.FloorAmount;
            existing.PrincipalPercent = req.PrincipalPercent;
            existing.CeilingAmount = req.CeilingAmount;
            existing.IncludeInterest = req.IncludeInterest;
            existing.IncludeFees = req.IncludeFees;
        }

        if (req.IsDefault)
        {
            var others = await _db.MinimumPaymentPolicies.Where(x => x.Code != req.Code && x.IsDefault).ToListAsync(cancellationToken);
            foreach (var o in others)
                o.IsDefault = false;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Results.Ok(existing);
    }
}
