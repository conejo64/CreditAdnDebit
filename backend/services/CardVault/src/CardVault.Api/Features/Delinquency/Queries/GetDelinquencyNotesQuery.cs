using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Collections;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Features.Delinquency.Queries;

/// <summary>
/// Returns all internal notes for a delinquency record, sorted by creation timestamp descending.
/// </summary>
public record GetDelinquencyNotesQuery(Guid DelinquencyRecordId)
    : IRequest<IReadOnlyList<DelinquencyNoteEntity>>;

public sealed class GetDelinquencyNotesQueryHandler
    : IRequestHandler<GetDelinquencyNotesQuery, IReadOnlyList<DelinquencyNoteEntity>>
{
    private readonly CardVaultDbContext _db;

    public GetDelinquencyNotesQueryHandler(CardVaultDbContext db) => _db = db;

    public async Task<IReadOnlyList<DelinquencyNoteEntity>> Handle(
        GetDelinquencyNotesQuery request,
        CancellationToken cancellationToken)
    {
        return await _db.DelinquencyNotes
            .Where(n => n.DelinquencyRecordId == request.DelinquencyRecordId)
            .OrderByDescending(n => n.CreatedOn)
            .ToListAsync(cancellationToken);
    }
}
