using IsoSwitch.Api.Security;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Tests.Security;

/// <summary>
/// Unit tests for TokenizationOptionsValidator (ADR-1 validator matrix).
/// Verifies SEC-1: rejects empty, short, DEV placeholder, and CHANGE_ME values;
/// accepts a valid 32+ character non-placeholder secret.
/// </summary>
public class TokenizationOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(string? secret)
    {
        var validator = new TokenizationOptionsValidator();
        var opts = new TokenizationOptions { Secret = secret ?? string.Empty };
        return validator.Validate(null, opts);
    }

    [Fact]
    public void EmptySecret_ReturnsFail()
    {
        var result = Validate(string.Empty);
        Assert.True(result.Failed);
    }

    [Fact]
    public void WhitespaceSecret_ReturnsFail()
    {
        var result = Validate("   ");
        Assert.True(result.Failed);
    }

    [Fact]
    public void ThirtyOneCharSecret_ReturnsFail()
    {
        var result = Validate(new string('x', 31));
        Assert.True(result.Failed);
    }

    [Fact]
    public void DevOnlyPlaceholder_ReturnsFail()
    {
        var result = Validate("DEV_ONLY_CHANGE_ME");
        Assert.True(result.Failed);
    }

    [Fact]
    public void ChangeMePlaceholder_ReturnsFail()
    {
        var result = Validate("CHANGE_ME_IN_PRODUCTION_32_CHARS_MIN");
        Assert.True(result.Failed);
    }

    [Fact]
    public void PlaceholderSubstring_ReturnsFail()
    {
        var result = Validate("this_is_a_placeholder_key_of_appropriate_length");
        Assert.True(result.Failed);
    }

    [Fact]
    public void Valid32CharRandomSecret_ReturnsSuccess()
    {
        var result = Validate("xK9mP2nQ5rT8vW1yZ4bD7gH0aE3fI6jL9");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Valid48CharRandomSecret_ReturnsSuccess()
    {
        var result = Validate("aB3cD4eF5gH6iJ7kL8mN9oP0qR1sT2uV3wX4yZ5aA6bB7c");
        Assert.True(result.Succeeded);
    }
}
