using CardVault.Api.Services.Notifications.Templates;
using FluentAssertions;

namespace CardVault.Tests.Features.Notifications.Templates;

/// <summary>
/// Unit tests for the PCI pre-render guard.
/// The guard MUST fail closed — any violation throws PciTemplateViolationException.
/// </summary>
public sealed class PciTemplateGuardTests
{
    private readonly PciTemplateGuard _sut = new();

    // ── Passing cases ────────────────────────────────────────────────────────

    [Fact]
    public void Validate_MaskedPan_Passes()
    {
        // masked PAN is 4 digits: ****1234
        var model = ValidModel() with { MaskedPan = "****1234" };
        // must not throw
        _sut.Validate(model);
    }

    [Fact]
    public void Validate_NullMaskedPan_Passes()
    {
        var model = ValidModel() with { MaskedPan = null };
        _sut.Validate(model);
    }

    [Fact]
    public void Validate_ShortNumericLastFour_DoesNotTriggerRegex()
    {
        // Last-4 alone (e.g. from masked PAN suffix): 4 digits — below the \d{6,} threshold
        var model = ValidModel() with { MaskedPan = "1234" };
        _sut.Validate(model);
    }

    [Fact]
    public void Validate_FiveDigitSequence_DoesNotTriggerRegex()
    {
        // 5 consecutive digits: below threshold (threshold is 6)
        var model = ValidModel() with { MaskedPan = "12345" };
        _sut.Validate(model);
    }

    [Fact]
    public void Validate_DisplayableOtpCode_Passes()
    {
        // A 6-digit OTP display code — NOTE: this MUST be allowed (it IS the display code)
        // The guard blocks OTP seed/secret fields, not the display OTP itself
        var model = ValidModel() with { OtpCode = "123456" };
        _sut.Validate(model);
    }

    [Fact]
    public void Validate_AmountAndCurrency_Pass()
    {
        var model = ValidModel() with { Amount = 99.99m, CurrencyCode = "USD" };
        _sut.Validate(model);
    }

    [Fact]
    public void Validate_MaskedMerchant_Passes()
    {
        var model = ValidModel() with { MaskedMerchant = "AMAZ***" };
        _sut.Validate(model);
    }

    // ── Blocking cases ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("4111111111111111")]  // full 16-digit PAN
    [InlineData("4111-1111-1111-1111")]  // hyphenated PAN
    [InlineData("411111111111")]  // 12-digit number
    [InlineData("123456")]  // 6 consecutive digits triggers the guard on MaskedPan
    public void Validate_UnmaskedPanInMaskedPanField_Throws(string rawPan)
    {
        var model = ValidModel() with { MaskedPan = rawPan };
        var act = () => _sut.Validate(model);
        act.Should().Throw<PciTemplateViolationException>()
            .Which.FieldName.Should().Be(nameof(TemplateModel.MaskedPan));
    }

    [Fact]
    public void Validate_OtpSecretInAdditionalDataField_Throws()
    {
        // Simulate a caller putting a raw OTP secret in AdditionalData
        var model = ValidModel() with { AdditionalData = "otp_secret:JBSWY3DPEHPK3PXP" };
        var act = () => _sut.Validate(model);
        act.Should().Throw<PciTemplateViolationException>();
    }

    [Fact]
    public void Validate_RawPanInAdditionalData_Throws()
    {
        var model = ValidModel() with { AdditionalData = "pan=4111111111111111" };
        var act = () => _sut.Validate(model);
        act.Should().Throw<PciTemplateViolationException>();
    }

    [Fact]
    public void Validate_LongDigitRunInMerchantField_Throws()
    {
        // 6+ consecutive digits in merchant name is suspicious
        var model = ValidModel() with { MaskedMerchant = "SHOP123456789" };
        var act = () => _sut.Validate(model);
        act.Should().Throw<PciTemplateViolationException>()
            .Which.FieldName.Should().Be(nameof(TemplateModel.MaskedMerchant));
    }

    [Fact]
    public void Validate_OtpSeedKeyword_InAdditionalDataThrows()
    {
        // keyword "secret" with an alphanumeric string suggests an OTP seed
        var model = ValidModel() with { AdditionalData = "totp_secret=BASE32VALUE" };
        var act = () => _sut.Validate(model);
        act.Should().Throw<PciTemplateViolationException>();
    }

    [Fact]
    public void Validate_NullModel_Throws()
    {
        var act = () => _sut.Validate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── PciTemplateViolationException contract ───────────────────────────────

    [Fact]
    public void PciTemplateViolationException_ExposesFieldName()
    {
        var model = ValidModel() with { MaskedPan = "4111111111111111" };
        PciTemplateViolationException? ex = null;
        try { _sut.Validate(model); }
        catch (PciTemplateViolationException e) { ex = e; }

        ex.Should().NotBeNull();
        ex!.FieldName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PciTemplateViolationException_MessageDoesNotLeakValue()
    {
        // The exception message must NOT echo back the raw PAN value
        var rawPan = "4111111111111111";
        var model = ValidModel() with { MaskedPan = rawPan };
        PciTemplateViolationException? ex = null;
        try { _sut.Validate(model); }
        catch (PciTemplateViolationException e) { ex = e; }

        ex.Should().NotBeNull();
        ex!.Message.Should().NotContain(rawPan, "exception message must not echo raw PAN");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TemplateModel ValidModel() =>
        new(
            TemplateType: "OTP",
            Locale: "es-EC",
            MaskedPan: null,
            Amount: null,
            CurrencyCode: null,
            MaskedMerchant: null,
            Timestamp: null,
            OtpCode: null,
            AdditionalData: null);
}
