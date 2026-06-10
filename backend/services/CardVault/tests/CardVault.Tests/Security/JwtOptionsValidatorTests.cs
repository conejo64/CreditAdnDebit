using CardVault.Api.Security;
using Microsoft.Extensions.Options;

namespace CardVault.Tests.Security;

/// <summary>
/// Unit tests for JwtOptionsValidator (ADR-1 validator matrix).
/// Verifies SEC-2: rejects empty, short, DEV placeholder, and CHANGE_ME values;
/// accepts a valid 32+ character non-placeholder key.
/// </summary>
public class JwtOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(string? key)
    {
        var validator = new JwtOptionsValidator();
        var opts = new JwtOptions { SigningKey = key ?? string.Empty };
        return validator.Validate(null, opts);
    }

    [Fact]
    public void EmptyKey_ReturnsFail()
    {
        var result = Validate(string.Empty);
        Assert.True(result.Failed);
    }

    [Fact]
    public void WhitespaceKey_ReturnsFail()
    {
        var result = Validate("   ");
        Assert.True(result.Failed);
    }

    [Fact]
    public void ThirtyOneCharKey_ReturnsFail()
    {
        var result = Validate(new string('x', 31));
        Assert.True(result.Failed);
    }

    [Fact]
    public void DevOnlyPlaceholder_ReturnsFail()
    {
        var result = Validate("DEV_ONLY_change_me_please_32+chars");
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
        var result = Validate("this_is_a_placeholder_key_for_dev_usage");
        Assert.True(result.Failed);
    }

    [Fact]
    public void Valid32CharRandomKey_ReturnsSuccess()
    {
        var result = Validate("xK9!mP2@nQ5#rT8$vW1&yZ4*bD7^gH0_");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Valid48CharRandomKey_ReturnsSuccess()
    {
        var result = Validate("aB3cD4eF5gH6iJ7kL8mN9oP0qR1sT2uV3wX4yZ5aA6bB7c");
        Assert.True(result.Succeeded);
    }
}
