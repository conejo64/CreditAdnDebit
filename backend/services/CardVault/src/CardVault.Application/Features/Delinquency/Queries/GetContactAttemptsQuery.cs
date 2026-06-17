using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Collections;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Features.Delinquency.Queries;

/// <summary>
/// Returns all contact attempts for a delinquency record, sorted by timestamp descending.
/// </summary>
public record GetContactAttemptsQuery(Guid DelinquencyRecordId)
    : IRequest<IReadOnlyList<ContactAttemptEntity>>;

public sealed class GetContactAttemptsQueryHandler
    : IRequestHandler<GetContactAttemptsQuery, IReadOnlyList<ContactAttemptEntity>>
{
    private readonly CardVaultDbContext _db;

    public GetContactAttemptsQueryHandler(CardVaultDbContext db) => _db = db;

    public async Task<IReadOnlyList<ContactAttemptEntity>> Handle(
        GetContactAttemptsQuery request,
        CancellationToken cancellationToken)
    {
        return await _db.ContactAttempts
            .Where(a => a.DelinquencyRecordId == request.DelinquencyRecordId)
            .OrderByDescending(a => a.AttemptedOn)
            .ToListAsync(cancellationToken);
    }
}
