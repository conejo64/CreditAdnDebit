using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Application.Services;

namespace CardVault.Application.Features.Billing.Queries;

public record GetStatementQuery(Guid Id) : IRequest<IResult>;
public record GetActiveInstallmentPlansQuery(Guid AccountId) : IRequest<IResult>;
public class GetStatementQueryHandler : IRequestHandler<GetStatementQuery, IResult>
{
    private readonly BillingService _billing;

    public GetStatementQueryHandler(BillingService billing)
    {
        _billing = billing;
    }

    public async Task<IResult> Handle(GetStatementQuery request, CancellationToken cancellationToken)
    {
        var st = await _billing.GetStatementAsync(request.Id, cancellationToken);
        if (st is null)
            return Results.NotFound();
        var lines = await _billing.GetLinesAsync(request.Id, cancellationToken);
        return Results.Ok(new { statement = st, lines });
    }
}

public class GetActiveInstallmentPlansQueryHandler : IRequestHandler<GetActiveInstallmentPlansQuery, IResult>
{
    private readonly InstallmentService _installments;

    public GetActiveInstallmentPlansQueryHandler(InstallmentService installments) => _installments = installments;

    public async Task<IResult> Handle(GetActiveInstallmentPlansQuery request, CancellationToken ct)
    {
        var list = await _installments.GetActivePlansAsync(request.AccountId, ct);
        return Results.Ok(list);
    }
}

public record GetStatementsByAccountQuery(Guid AccountId, int Take) : IRequest<IResult>;
public class GetStatementsByAccountQueryHandler : IRequestHandler<GetStatementsByAccountQuery, IResult>
{
    private readonly BillingService _billing;

    public GetStatementsByAccountQueryHandler(BillingService billing)
    {
        _billing = billing;
    }

    public async Task<IResult> Handle(GetStatementsByAccountQuery request, CancellationToken cancellationToken)
    {
        int take = request.Take <= 0 ? 20 : Math.Min(request.Take, 200);
        var list = await _billing.GetStatementsForAccountAsync(request.AccountId, take, cancellationToken);
        return Results.Ok(list);
    }
}

public record GetStatementBucketsQuery(Guid Id) : IRequest<IResult>;
public class GetStatementBucketsQueryHandler : IRequestHandler<GetStatementBucketsQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public GetStatementBucketsQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(GetStatementBucketsQuery request, CancellationToken cancellationToken)
    {
        var st = await _db.Statements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (st is null)
            return Results.NotFound();
        return Results.Ok(new { 
            statementId = st.Id, 
            st.AccountId, 
            due = new { principal = st.PrincipalDue, interest = st.InterestDue, fees = st.FeesDue }, 
            paid = new { principal = st.PaidToPrincipal, interest = st.PaidToInterest, fees = st.PaidToFees }, 
            st.PaidAmount, 
            st.MinimumPayment, 
            st.TotalPaymentDue 
        });
    }
}

public record PrintStatementQuery(Guid Id) : IRequest<IResult>;
public class PrintStatementQueryHandler : IRequestHandler<PrintStatementQuery, IResult>
{
    private readonly BillingService _billing;

    public PrintStatementQueryHandler(BillingService billing)
    {
        _billing = billing;
    }

    public async Task<IResult> Handle(PrintStatementQuery request, CancellationToken cancellationToken)
    {
        var st = await _billing.GetStatementAsync(request.Id, cancellationToken);
        if (st is null)
            return Results.NotFound();
        var lines = await _billing.GetLinesAsync(request.Id, cancellationToken);
        var text = $"STATEMENT {st.Id}\nAccount: {st.AccountId}\nCycle: {st.CycleStart:yyyy-MM-dd}..{st.CycleEnd:yyyy-MM-dd}\nStatementDate: {st.StatementDate:yyyy-MM-dd}\nDueDate: {st.DueDate:yyyy-MM-dd}\n\nPrevBalance: {st.PreviousBalance}\nPurchases: {st.Purchases}\nPayments: {st.Payments}\nFees: {st.Fees}\nInterest: {st.Interest}\nNewBalance: {st.NewBalance}\nMinPayment: {st.MinimumPayment}\nPaidAmount: {st.PaidAmount}\nLateFeeAppliedOn: {st.LateFeeAppliedOn}\n\nLINES:\n";
        foreach (var l in lines)
            text += $"{l.PostedOn:yyyy-MM-dd} | {l.Type} | {l.Amount} | {l.Description}\n";
        return Results.Text(text, "text/plain");
    }
}

public record GetStatementPdfQuery(Guid Id) : IRequest<IResult>;
public class GetStatementPdfQueryHandler : IRequestHandler<GetStatementPdfQuery, IResult>
{
    private readonly StatementPdfService _pdf;

    public GetStatementPdfQueryHandler(StatementPdfService pdf)
    {
        _pdf = pdf;
    }

    public async Task<IResult> Handle(GetStatementPdfQuery request, CancellationToken cancellationToken)
    {
        var bytes = await _pdf.GenerateAsync(request.Id, cancellationToken);
        return Results.File(bytes, "application/pdf", fileDownloadName: $"statement-{request.Id}.pdf");
    }
}

public record GetMinimumPaymentPoliciesQuery() : IRequest<IResult>;
public class GetMinimumPaymentPoliciesQueryHandler : IRequestHandler<GetMinimumPaymentPoliciesQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public GetMinimumPaymentPoliciesQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(GetMinimumPaymentPoliciesQuery request, CancellationToken cancellationToken)
    {
        var list = await _db.MinimumPaymentPolicies.AsNoTracking().OrderByDescending(x => x.IsDefault).ThenBy(x => x.Code).ToListAsync(cancellationToken);
        return Results.Ok(list);
    }
}
