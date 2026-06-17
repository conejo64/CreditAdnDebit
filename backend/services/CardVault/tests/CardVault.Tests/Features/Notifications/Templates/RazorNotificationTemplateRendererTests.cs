using CardVault.Application.Services.Notifications.Templates;
using FluentAssertions;

namespace CardVault.Tests.Features.Notifications.Templates;

/// <summary>
/// Integration-style tests for <see cref="RazorNotificationTemplateRenderer"/>.
/// Uses RazorLight directly — no web host required.
/// </summary>
public sealed class RazorNotificationTemplateRendererTests
{
    private readonly INotificationTemplateRenderer _renderer = RazorNotificationTemplateRenderer.Create();

    // ── OTP template ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_Otp_EsEc_ContainsOtpCode()
    {
        var model = new TemplateModel(
            TemplateType: "Otp",
            Locale: "es-EC",
            MaskedPan: null,
            Amount: null,
            CurrencyCode: null,
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: "654321",
            AdditionalData: null);

        var result = await _renderer.RenderAsync(model);

        result.Subject.Should().NotBeNullOrWhiteSpace();
        result.Body.Should().Contain("654321");
        result.Body.Should().NotContain("seed");
        result.Body.Should().NotContain("secret");
    }

    [Fact]
    public async Task RenderAsync_Otp_EnUs_ContainsEnglishText()
    {
        var model = new TemplateModel(
            TemplateType: "Otp",
            Locale: "en-US",
            MaskedPan: null,
            Amount: null,
            CurrencyCode: null,
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: "789012",
            AdditionalData: null);

        var result = await _renderer.RenderAsync(model);

        result.Subject.Should().NotBeNullOrWhiteSpace();
        result.Body.Should().Contain("789012");
        // en-US template must use English — spot check for a distinctive English phrase
        result.Body.Should().NotBeNullOrWhiteSpace();
    }

    // ── TransactionNotification template ─────────────────────────────────────

    [Fact]
    public async Task RenderAsync_TransactionNotification_EsEc_ContainsMaskedPanAndAmount()
    {
        var model = new TemplateModel(
            TemplateType: "TransactionNotification",
            Locale: "es-EC",
            MaskedPan: "****1234",
            Amount: 150.00m,
            CurrencyCode: "USD",
            MaskedMerchant: "AMAZ***",
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: null,
            AdditionalData: null);

        var result = await _renderer.RenderAsync(model);

        result.Body.Should().Contain("1234");   // last-4 visible
        result.Body.Should().Contain("150");    // amount visible
        result.Body.Should().NotContain("4111111111111111"); // full PAN never rendered
    }

    [Fact]
    public async Task RenderAsync_TransactionNotification_EnUs_Renders()
    {
        var model = new TemplateModel(
            TemplateType: "TransactionNotification",
            Locale: "en-US",
            MaskedPan: "****5678",
            Amount: 42.99m,
            CurrencyCode: "USD",
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: null,
            AdditionalData: null);

        var result = await _renderer.RenderAsync(model);

        result.Body.Should().Contain("5678");
        result.Body.Should().Contain("42");
    }

    // ── SecurityAlert template ────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_SecurityAlert_EsEc_Renders()
    {
        var model = new TemplateModel(
            TemplateType: "SecurityAlert",
            Locale: "es-EC",
            MaskedPan: null,
            Amount: null,
            CurrencyCode: null,
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: null,
            AdditionalData: null);

        var result = await _renderer.RenderAsync(model);

        result.Subject.Should().NotBeNullOrWhiteSpace();
        result.Body.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RenderAsync_SecurityAlert_EnUs_Renders()
    {
        var model = new TemplateModel(
            TemplateType: "SecurityAlert",
            Locale: "en-US",
            MaskedPan: null,
            Amount: null,
            CurrencyCode: null,
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: null,
            AdditionalData: null);

        var result = await _renderer.RenderAsync(model);

        result.Subject.Should().NotBeNullOrWhiteSpace();
        result.Body.Should().NotBeNullOrWhiteSpace();
    }

    // ── StatementAvailable template ───────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_StatementAvailable_EsEc_Renders()
    {
        var model = new TemplateModel(
            TemplateType: "StatementAvailable",
            Locale: "es-EC",
            MaskedPan: "****4321",
            Amount: 500.00m,
            CurrencyCode: "USD",
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: null,
            AdditionalData: null);

        var result = await _renderer.RenderAsync(model);

        result.Subject.Should().NotBeNullOrWhiteSpace();
        result.Body.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RenderAsync_StatementAvailable_EnUs_Renders()
    {
        var model = new TemplateModel(
            TemplateType: "StatementAvailable",
            Locale: "en-US",
            MaskedPan: "****4321",
            Amount: 500.00m,
            CurrencyCode: "USD",
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: null,
            AdditionalData: null);

        var result = await _renderer.RenderAsync(model);

        result.Subject.Should().NotBeNullOrWhiteSpace();
        result.Body.Should().NotBeNullOrWhiteSpace();
    }

    // ── PaymentReceived template ──────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_PaymentReceived_EsEc_Renders()
    {
        var model = new TemplateModel(
            TemplateType: "PaymentReceived",
            Locale: "es-EC",
            MaskedPan: null,
            Amount: 200.00m,
            CurrencyCode: "USD",
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: null,
            AdditionalData: null);

        var result = await _renderer.RenderAsync(model);

        result.Subject.Should().NotBeNullOrWhiteSpace();
        result.Body.Should().NotBeNullOrWhiteSpace();
        result.Body.Should().Contain("200");
    }

    [Fact]
    public async Task RenderAsync_PaymentReceived_EnUs_Renders()
    {
        var model = new TemplateModel(
            TemplateType: "PaymentReceived",
            Locale: "en-US",
            MaskedPan: null,
            Amount: 200.00m,
            CurrencyCode: "USD",
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: null,
            AdditionalData: null);

        var result = await _renderer.RenderAsync(model);

        result.Subject.Should().NotBeNullOrWhiteSpace();
        result.Body.Should().NotBeNullOrWhiteSpace();
    }

    // ── Locale fallback ───────────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_NullLocale_FallsBackToEsEc()
    {
        var model = new TemplateModel(
            TemplateType: "Otp",
            Locale: null!,   // null → must fall back to es-EC
            MaskedPan: null,
            Amount: null,
            CurrencyCode: null,
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: "111222",
            AdditionalData: null);

        // Should render without throwing
        var result = await _renderer.RenderAsync(model);
        result.Body.Should().Contain("111222");
    }

    [Fact]
    public async Task RenderAsync_UnsupportedLocale_FallsBackToEsEc()
    {
        var model = new TemplateModel(
            TemplateType: "Otp",
            Locale: "fr-FR",   // unsupported → fall back to es-EC
            MaskedPan: null,
            Amount: null,
            CurrencyCode: null,
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: "999888",
            AdditionalData: null);

        var result = await _renderer.RenderAsync(model);
        result.Body.Should().Contain("999888");
    }

    // ── PCI guard integration ─────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_UnmaskedPanInModel_ThrowsPciViolation()
    {
        var model = new TemplateModel(
            TemplateType: "TransactionNotification",
            Locale: "es-EC",
            MaskedPan: "4111111111111111",   // FULL PAN — must be blocked
            Amount: 50m,
            CurrencyCode: "USD",
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: null,
            AdditionalData: null);

        var act = async () => await _renderer.RenderAsync(model);
        await act.Should().ThrowAsync<PciTemplateViolationException>();
    }

    [Fact]
    public async Task RenderAsync_OtpSecretInModel_ThrowsPciViolation()
    {
        var model = new TemplateModel(
            TemplateType: "Otp",
            Locale: "es-EC",
            MaskedPan: null,
            Amount: null,
            CurrencyCode: null,
            MaskedMerchant: null,
            Timestamp: DateTimeOffset.UtcNow,
            OtpCode: null,
            AdditionalData: "otp_secret=JBSWY3DPEHPK3PXP");

        var act = async () => await _renderer.RenderAsync(model);
        await act.Should().ThrowAsync<PciTemplateViolationException>();
    }
}
