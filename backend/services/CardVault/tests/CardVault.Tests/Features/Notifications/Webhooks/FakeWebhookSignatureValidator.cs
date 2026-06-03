using CardVault.Api.Services.Notifications;
using Microsoft.AspNetCore.Http;

namespace CardVault.Tests.Features.Notifications.Webhooks;

/// <summary>
/// Test double for <see cref="IWebhookSignatureValidator"/>.
/// Configured at construction time to always accept or always reject.
/// Used in integration tests to isolate the endpoint behaviour from the
/// cryptographic validation logic (which is covered by unit tests in 1e.1).
/// </summary>
public sealed class FakeWebhookSignatureValidator : IWebhookSignatureValidator
{
    /// <summary>The sentinel header the controller checks for "missing-signature" detection.</summary>
    public const string FakeSignatureHeaderName = "X-Fake-Signature";

    private readonly bool _alwaysResult;

    /// <param name="providerId">The provider key this fake serves (e.g. "twilio").</param>
    /// <param name="alwaysResult">
    ///   <c>true</c>  → <see cref="Validate"/> always returns valid (happy path tests).
    ///   <c>false</c> → <see cref="Validate"/> always returns invalid (tampered tests).
    /// </param>
    public FakeWebhookSignatureValidator(string providerId, bool alwaysResult = true)
    {
        ProviderId = providerId;
        _alwaysResult = alwaysResult;
    }

    /// <inheritdoc />
    public string ProviderId { get; }

    /// <inheritdoc />
    public string SignatureHeaderName => FakeSignatureHeaderName;

    /// <inheritdoc />
    public bool Validate(HttpRequest request, ReadOnlySpan<byte> rawBody) => _alwaysResult;
}
