using BuildingBlocks.Commercial;

namespace IsoSwitch.Tests.Commercial;

/// <summary>
/// Covers task 3.1: what the sanitized disclosure endpoint is allowed to say.
/// The register is internal and carries governance metadata; the payload that
/// leaves the service must not.
/// </summary>
public sealed class CommercialDisclosureTests
{
    private static ClaimRegisterEntry FullyPopulatedClaim() => new()
    {
        CapabilityId = "iso8583.acquirer.banred",
        Label = "Banred acquirer connectivity",
        Maturity = ClaimMaturity.Simulation,
        PermittedModes = [CommercialMode.Demo],
        CommercialMessage = "Available in demo environments only.",
        Owner = "payments-platform@example.test",
        EvidenceUri = new Uri("https://evidence.internal.example.test/banred/2026-08"),
        EvidenceHash = "9f2b7c1d",
        ReviewedBy = "risk-review@example.test",
        ReviewedAtUtc = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
        ExpiresAtUtc = new DateTimeOffset(2027, 8, 31, 0, 0, 0, TimeSpan.Zero),
        InternalNotes = "Pending certification slot with the acquirer."
    };

    [Fact]
    public void Build_CarriesTheModeAndRegisterVersionTheOperatorIsLookingAt()
    {
        var options = new CommercialOptions
        {
            Mode = CommercialMode.Commercial,
            ClaimRegisterVersion = "2026.08"
        };

        var response = CommercialDisclosure.Build(options, new StaticClaimRegister([FullyPopulatedClaim()]));

        Assert.Equal(CommercialMode.Commercial, response.Mode);
        Assert.Equal("2026.08", response.ClaimRegisterVersion);
    }

    [Fact]
    public void Build_ExposesOnlyThePermittedDisclosureFields()
    {
        var options = new CommercialOptions { ClaimRegisterVersion = "2026.08" };

        var claim = Assert.Single(CommercialDisclosure.Build(options, new StaticClaimRegister([FullyPopulatedClaim()])).Claims);

        Assert.Equal("iso8583.acquirer.banred", claim.CapabilityId);
        Assert.Equal("Banred acquirer connectivity", claim.Label);
        Assert.Equal(ClaimMaturity.Simulation, claim.Maturity);
        Assert.Equal([CommercialMode.Demo], claim.PermittedModes);
        Assert.Equal("Available in demo environments only.", claim.CommercialMessage);

        // The DTO is the contract boundary: anything the register knows beyond
        // these five fields must have no way to reach a caller.
        var exposed = claim.GetType().GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("Owner", exposed);
        Assert.DoesNotContain("EvidenceUri", exposed);
        Assert.DoesNotContain("EvidenceHash", exposed);
        Assert.DoesNotContain("ReviewedBy", exposed);
        Assert.DoesNotContain("ReviewedAtUtc", exposed);
        Assert.DoesNotContain("ExpiresAtUtc", exposed);
        Assert.DoesNotContain("InternalNotes", exposed);
    }

    [Fact]
    public void Build_KeepsClaimsThatThisModeDoesNotPermit()
    {
        // Withholding them would leave the operator unable to see WHY an action is
        // missing, which is the disclosure this requirement exists to provide.
        var options = new CommercialOptions
        {
            Mode = CommercialMode.Commercial,
            ClaimRegisterVersion = "2026.08"
        };

        var claim = Assert.Single(CommercialDisclosure.Build(options, new StaticClaimRegister([FullyPopulatedClaim()])).Claims);

        Assert.DoesNotContain(CommercialMode.Commercial, claim.PermittedModes);
    }

    [Fact]
    public void Build_OnAnEmptyRegisterStillReportsTheMode()
    {
        var options = new CommercialOptions
        {
            Mode = CommercialMode.Commercial,
            ClaimRegisterVersion = "unpublished"
        };

        var response = CommercialDisclosure.Build(options, new StaticClaimRegister([]));

        Assert.Empty(response.Claims);
        Assert.Equal(CommercialMode.Commercial, response.Mode);
        Assert.Equal("unpublished", response.ClaimRegisterVersion);
    }

    [Fact]
    public void StaticClaimRegister_RejectsAClaimTheRegisterValidatorWouldFail()
    {
        // A register that accepts unreviewed claims would let an unevidenced
        // capability be disclosed as verified, which the governance spec forbids.
        var unevidenced = new ClaimRegisterEntry
        {
            CapabilityId = "iso8583.acquirer.datafast",
            Label = "Datafast acquirer connectivity",
            Maturity = ClaimMaturity.Verified,
            PermittedModes = [CommercialMode.Commercial],
            CommercialMessage = "Certified with the acquirer.",
            Owner = "payments-platform@example.test",
            ReviewedBy = "risk-review@example.test"
        };

        Assert.Throws<ArgumentException>(() => new StaticClaimRegister([unevidenced]));
    }
}
