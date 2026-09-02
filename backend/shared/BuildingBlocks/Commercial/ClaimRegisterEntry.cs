namespace BuildingBlocks.Commercial;

public sealed class ClaimRegisterEntry
{
    public required string CapabilityId { get; init; }

    public required string Label { get; init; }

    public ClaimMaturity Maturity { get; init; } = ClaimMaturity.Roadmap;

    public IReadOnlyCollection<CommercialMode> PermittedModes { get; init; } = [];

    public required string CommercialMessage { get; init; }

    public required string Owner { get; init; }

    public Uri? EvidenceUri { get; init; }

    public string? EvidenceHash { get; init; }

    public required string ReviewedBy { get; init; }

    public DateTimeOffset? ReviewedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public string? InternalNotes { get; init; }
}
