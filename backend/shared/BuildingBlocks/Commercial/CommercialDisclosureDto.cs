namespace BuildingBlocks.Commercial;

public sealed record CommercialDisclosureDto(
    string CapabilityId,
    string Label,
    ClaimMaturity Maturity,
    IReadOnlyCollection<CommercialMode> PermittedModes,
    string CommercialMessage)
{
    public static CommercialDisclosureDto FromClaim(ClaimRegisterEntry claim)
    {
        ArgumentNullException.ThrowIfNull(claim);

        return new CommercialDisclosureDto(
            claim.CapabilityId,
            claim.Label,
            claim.Maturity,
            claim.PermittedModes.ToArray(),
            claim.CommercialMessage);
    }
}
