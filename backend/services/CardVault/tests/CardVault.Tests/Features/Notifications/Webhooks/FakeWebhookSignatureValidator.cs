using CardVault.Api.Services.Notifications;
using Microsoft.AspNetCore.Http;

namespace CardVault.Tests.Features.Notifications.Webhooks;

/// <summary>
/// Test double for <see cref="IWebhookSignatureValidator"/>.
/// Configured at construction time to always return a fixed <see cref="WebhookValidationResult"/>.
/// Used in integration tests to isolate the endpoint behaviour from the
/// cryptographic validation logic (which is covered by unit tests in 1e.1).
///
/// NOTE: body-content correctness is verified in 1e.1 unit tests, not here.
/// This fake ignores rawBody intentionally — its role is to control endpoint routing
/// and audit-reason assertions, not to re-test HMAC/ECDSA logic.
/// </summary>
public sealed class FakeWebhookSignatureValidator : IWebhookSignatureValidator
{
    /// <summary>The sentinel header the controller checks for "missing-signature" detection.</summary>
    public const string FakeSignatureHeaderName = "X-Fake-Signature";

    private readonly WebhookValidationResult _alwaysResult;

    /// <param name="providerId">The provider key this fake serves (e.g. "twilio").</param>
    /// <param name="alwaysResult">
    ///   The fixed <see cref="WebhookValidationResult"/> <see cref="Validate"/> always returns.
    ///   Defaults to <see cref="WebhookValidationResult.Valid"/> (happy-path tests).
    /// </param>
    public FakeWebhookSignatureValidator(
        string providerId,
        WebhookValidationResult alwaysResult = WebhookValidationResult.Valid)
    {
        ProviderId = providerId;
        _alwaysResult = alwaysResult;
    }

    /// <summary>
    /// Convenience constructor for tests that only need valid/invalid without caring about
    /// the specific failure variant.
    /// </summary>
    /// <param name="providerId">The provider key this fake serves.</param>
    /// <param name="valid">
    ///   <c>true</c>  → returns <see cref="WebhookValidationResult.Valid"/>.
    ///   <c>false</c> → returns <see cref="WebhookValidationResult.InvalidSignature"/>.
    /// </param>
    public FakeWebhookSignatureValidator(string providerId, bool valid)
        : this(providerId, valid ? WebhookValidationResult.Valid : WebhookValidationResult.InvalidSignature)
    {
    }

    /// <inheritdoc />
    public string ProviderId { get; }

    /// <inheritdoc />
    public string SignatureHeaderName => FakeSignatureHeaderName;

    /// <inheritdoc />
    public WebhookValidationResult Validate(HttpRequest request, ReadOnlySpan<byte> rawBody) => _alwaysResult;
}
