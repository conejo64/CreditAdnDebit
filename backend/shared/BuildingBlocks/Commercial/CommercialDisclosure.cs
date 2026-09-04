namespace BuildingBlocks.Commercial;

/// <summary>
/// What a caller is told about the running mode and the governed capability claims.
/// </summary>
public sealed record CommercialDisclosureResponse(
    CommercialMode Mode,
    string ClaimRegisterVersion,
    IReadOnlyList<CommercialDisclosureDto> Claims);

/// <summary>
/// Turns the internal claim register into the sanitized payload a service may publish.
/// </summary>
public static class CommercialDisclosure
{
    /// <summary>
    /// Projects every claim through <see cref="CommercialDisclosureDto.FromClaim"/> and
    /// states the mode the caller is looking at.
    /// </summary>
    /// <remarks>
    /// Claims the running mode does not permit are kept rather than filtered out. The
    /// operator needs to see that a capability exists and why it is unavailable here;
    /// dropping it would leave an action simply missing from the UI with no explanation,
    /// which is the opposite of the disclosure this serves. Deciding what to disable is
    /// the caller's job, and <see cref="CommercialDisclosureDto.PermittedModes"/> plus
    /// <see cref="CommercialDisclosureResponse.Mode"/> are what it needs to do it.
    /// </remarks>
    public static CommercialDisclosureResponse Build(CommercialOptions options, IClaimRegister register)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(register);

        var claims = register.GetClaims()
            .Select(CommercialDisclosureDto.FromClaim)
            .ToArray();

        return new CommercialDisclosureResponse(options.Mode, options.ClaimRegisterVersion, claims);
    }
}
