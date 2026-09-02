using BuildingBlocks;

namespace BuildingBlocks.Commercial;

public static class ClaimRegisterValidator
{
    public static Result Validate(IEnumerable<ClaimRegisterEntry> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        foreach (var claim in claims)
        {
            if (string.IsNullOrWhiteSpace(claim.CapabilityId))
            {
                return Result.Fail("Claim capability id is required.");
            }

            if (string.IsNullOrWhiteSpace(claim.Label))
            {
                return Result.Fail($"Claim '{claim.CapabilityId}' label is required.");
            }

            if (string.IsNullOrWhiteSpace(claim.CommercialMessage))
            {
                return Result.Fail($"Claim '{claim.CapabilityId}' commercial message is required.");
            }

            if (string.IsNullOrWhiteSpace(claim.Owner))
            {
                return Result.Fail($"Claim '{claim.CapabilityId}' owner is required.");
            }

            if (string.IsNullOrWhiteSpace(claim.ReviewedBy))
            {
                return Result.Fail($"Claim '{claim.CapabilityId}' reviewer is required.");
            }

            if (claim.PermittedModes.Count == 0)
            {
                return Result.Fail($"Claim '{claim.CapabilityId}' must declare at least one permitted mode.");
            }

            if (claim.Maturity == ClaimMaturity.Verified && claim.EvidenceUri is null && string.IsNullOrWhiteSpace(claim.EvidenceHash))
            {
                return Result.Fail($"Claim '{claim.CapabilityId}' is verified but has no evidence.");
            }
        }

        return Result.Ok();
    }
}
