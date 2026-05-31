using CardVault.Api.Vault;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace CardVault.Tests.Features.Vault;

/// <summary>
/// TDD (RED before T-01 implementation): verifies that VaultOptions binds an
/// AdminRateLimit nested section correctly and has meaningful code defaults.
/// </summary>
public sealed class VaultOptionsAdminRateLimitTests
{
    [Fact]
    public void AdminRateLimit_BindsFromConfiguration_ProducesExpectedValues()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:AdminRateLimit:PermitLimit"]   = "5",
                ["Vault:AdminRateLimit:WindowSeconds"] = "300",
                ["Vault:AdminRateLimit:QueueLimit"]    = "0"
            })
            .Build();

        // Act
        var opt = config.GetSection("Vault").Get<VaultOptions>()!;

        // Assert
        opt.AdminRateLimit.Should().NotBeNull();
        opt.AdminRateLimit.PermitLimit.Should().Be(5);
        opt.AdminRateLimit.WindowSeconds.Should().Be(300);
        opt.AdminRateLimit.QueueLimit.Should().Be(0);
    }

    [Fact]
    public void AdminRateLimit_UnboundInstance_HasNonZeroCodeDefaults()
    {
        // Act — default-constructed (no config bound)
        var opt = new VaultOptions();

        // Assert — code defaults must be sensible (at least 1)
        opt.AdminRateLimit.PermitLimit.Should().BeGreaterThanOrEqualTo(1);
        opt.AdminRateLimit.WindowSeconds.Should().BeGreaterThanOrEqualTo(1);
        // QueueLimit == 0 is valid (no queuing; surfaces 429 immediately)
        opt.AdminRateLimit.QueueLimit.Should().BeGreaterThanOrEqualTo(0);
    }
}
