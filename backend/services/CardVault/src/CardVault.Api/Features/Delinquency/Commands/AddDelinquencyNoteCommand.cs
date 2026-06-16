using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Collections;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Features.Delinquency.Commands;

/// <summary>
/// Adds an internal note to an active delinquency record.
/// Returns the ID of the newly created <see cref="DelinquencyNoteEntity"/>.
/// </summary>
public record AddDelinquencyNoteCommand(
    Guid DelinquencyRecordId,
    string Content,
    string CreatedBy
) : IRequest<Guid>;

public sealed class AddDelinquencyNoteCommandHandler
    : IRequestHandler<AddDelinquencyNoteCommand, Guid>
{
    private readonly CardVaultDbContext _db;

    public AddDelinquencyNoteCommandHandler(CardVaultDbContext db) => _db = db;

    public async Task<Guid> Handle(
        AddDelinquencyNoteCommand request,
        CancellationToken cancellationToken)
    {
        var record = await _db.DelinquencyRecords
            .FirstOrDefaultAsync(r => r.Id == request.DelinquencyRecordId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Delinquency record {request.DelinquencyRecordId} not found.");

        if (record.Status == DelinquencyRecordStatus.Resolved)
            throw new InvalidOperationException(
                $"Delinquency record {request.DelinquencyRecordId} is resolved and immutable.");

        var note = new DelinquencyNoteEntity
        {
            DelinquencyRecordId = record.Id,
            Content             = request.Content,
            CreatedBy           = request.CreatedBy,
            CreatedOn           = DateTimeOffset.UtcNow,
        };

        _db.DelinquencyNotes.Add(note);
        await _db.SaveChangesAsync(cancellationToken);

        return note.Id;
    }
}
