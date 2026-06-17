using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Collections;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Features.Delinquency.Commands;

/// <summary>
/// Registers a contact attempt for an active delinquency record.
/// Returns the ID of the newly created <see cref="ContactAttemptEntity"/>.
/// </summary>
public record RegisterContactAttemptCommand(
    Guid DelinquencyRecordId,
    ContactChannel Channel,
    ContactOutcome Outcome,
    string? Notes,
    string AttemptedBy
) : IRequest<Guid>;

public sealed class RegisterContactAttemptCommandHandler
    : IRequestHandler<RegisterContactAttemptCommand, Guid>
{
    private readonly CardVaultDbContext _db;

    public RegisterContactAttemptCommandHandler(CardVaultDbContext db) => _db = db;

    public async Task<Guid> Handle(
        RegisterContactAttemptCommand request,
        CancellationToken cancellationToken)
    {
        var record = await _db.DelinquencyRecords
            .FirstOrDefaultAsync(r => r.Id == request.DelinquencyRecordId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Delinquency record {request.DelinquencyRecordId} not found.");

        if (record.Status == DelinquencyRecordStatus.Resolved)
            throw new InvalidOperationException(
                $"Delinquency record {request.DelinquencyRecordId} is resolved and immutable.");

        var attempt = new ContactAttemptEntity
        {
            DelinquencyRecordId = record.Id,
            Channel             = request.Channel,
            Outcome             = request.Outcome,
            Notes               = request.Notes,
            AttemptedBy         = request.AttemptedBy,
            AttemptedOn         = DateTimeOffset.UtcNow,
        };

        _db.ContactAttempts.Add(attempt);
        await _db.SaveChangesAsync(cancellationToken);

        return attempt.Id;
    }
}
