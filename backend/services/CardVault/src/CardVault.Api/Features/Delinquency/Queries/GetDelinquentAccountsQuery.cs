using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;

namespace CardVault.Api.Features.Delinquency.Queries;

// ─────────────────────────────────────────────────────────────────────────────
// Pagination wrapper — local to CardVault.Application scope per resolved decision
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Generic offset-pagination envelope.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; }
    public int TotalCount { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items      = items;
        TotalCount = totalCount;
        Page       = page;
        PageSize   = pageSize;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DTO
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Read-only projection of a delinquency record for the collections view.</summary>
public sealed class DelinquencyRecordDto
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public Guid StatementId { get; init; }
    public decimal OverdueAmount { get; init; }
    public int DaysInArrears { get; init; }

    /// <summary>Numeric bucket value (1=1-30, 2=31-60, 3=61-90, 4=>90).</summary>
    public int Bucket { get; init; }

    /// <summary>Human-readable bucket label.</summary>
    public string BucketLabel { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset UpdatedOn { get; init; }
    public DateTimeOffset? ResolvedOn { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Query record
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Read-only paginated query for delinquent accounts.</summary>
public sealed class GetDelinquentAccountsQuery : IRequest<IResult>
{
    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Maximum 200; defaults to 20.</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>Optional bucket filter: 1–4.</summary>
    public int? Bucket { get; init; }

    /// <summary>Delinquency record status filter. Defaults to "Active".</summary>
    public string Status { get; init; } = "Active";
}

// ─────────────────────────────────────────────────────────────────────────────
// Handler
// ─────────────────────────────────────────────────────────────────────────────

public sealed class GetDelinquentAccountsQueryHandler : IRequestHandler<GetDelinquentAccountsQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public GetDelinquentAccountsQueryHandler(CardVaultDbContext db) => _db = db;

    public async Task<IResult> Handle(GetDelinquentAccountsQuery request, CancellationToken ct)
    {
        // Normalize inputs
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        // Parse status filter — default Active
        var statusFilter = Enum.TryParse<DelinquencyRecordStatus>(request.Status, ignoreCase: true, out var parsedStatus)
            ? parsedStatus
            : DelinquencyRecordStatus.Active;

        // Build query
        var query = _db.DelinquencyRecords
            .AsNoTracking()
            .Where(r => r.Status == statusFilter);

        if (request.Bucket.HasValue && Enum.IsDefined(typeof(DelinquencyBucket), request.Bucket.Value))
        {
            var bucketEnum = (DelinquencyBucket)request.Bucket.Value;
            query = query.Where(r => r.Bucket == bucketEnum);
        }

        var totalCount = await query.CountAsync(ct);

        var records = await query
            .OrderByDescending(r => r.DaysInArrears)
            .ThenBy(r => r.AccountId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = records.Select(r => new DelinquencyRecordDto
        {
            Id            = r.Id,
            AccountId     = r.AccountId,
            StatementId   = r.StatementId,
            OverdueAmount = r.OverdueAmount,
            DaysInArrears = r.DaysInArrears,
            Bucket        = (int)r.Bucket,
            BucketLabel   = BucketLabel(r.Bucket),
            Status        = r.Status.ToString(),
            CreatedOn     = r.CreatedOn,
            UpdatedOn     = r.UpdatedOn,
            ResolvedOn    = r.ResolvedOn,
        }).ToList();

        var result = new PagedResult<DelinquencyRecordDto>(dtos, totalCount, page, pageSize);
        return Results.Ok(result);
    }

    private static string BucketLabel(DelinquencyBucket bucket) => bucket switch
    {
        DelinquencyBucket.DaysOneToThirty      => "1-30 days",
        DelinquencyBucket.DaysThirtyOneToSixty => "31-60 days",
        DelinquencyBucket.DaysSixtyOneToNinety => "61-90 days",
        DelinquencyBucket.OverNinety           => ">90 days",
        _                                      => "Unknown"
    };
}
