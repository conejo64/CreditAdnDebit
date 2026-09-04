namespace BuildingBlocks.Commercial;

/// <summary>
/// The internal source of governed capability claims.
/// </summary>
/// <remarks>
/// Entries carry owner, reviewer and evidence metadata, so a register instance must
/// never be serialised to a caller. <see cref="CommercialDisclosure"/> is the only
/// supported way to turn it into something that leaves the service.
/// </remarks>
public interface IClaimRegister
{
    IReadOnlyList<ClaimRegisterEntry> GetClaims();
}

/// <summary>
/// A register fixed at construction, which is where its entries are validated.
/// </summary>
/// <remarks>
/// Validating in the constructor is deliberate: an invalid register is a governance
/// defect, and the failure belongs at startup where it is visible, not at the first
/// request that happens to read it.
/// </remarks>
public sealed class StaticClaimRegister : IClaimRegister
{
    private readonly IReadOnlyList<ClaimRegisterEntry> _claims;

    public StaticClaimRegister(IReadOnlyList<ClaimRegisterEntry> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var validation = ClaimRegisterValidator.Validate(claims);
        if (!validation.IsSuccess)
        {
            throw new ArgumentException(validation.Error, nameof(claims));
        }

        _claims = claims;
    }

    public IReadOnlyList<ClaimRegisterEntry> GetClaims() => _claims;
}
