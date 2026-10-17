using System.Text.Json;
using CardVault.Application.Features.Auth.Commands;
using FluentAssertions;
using Xunit;

namespace CardVault.Tests.Security;

/// <summary>
/// G1 follow-up: the committed CardVault configuration must not widen the refresh-token
/// exposure window beyond the documented policy default (<see cref="RefreshTokenPolicy.DefaultLifetimeDays"/>).
/// Operators may lower the value per deployment; raising it is a documented decision, not a default.
/// </summary>
public class RefreshTokenLifetimePolicyTests
{
    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "backend")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root (containing 'backend/') not found.");
    }

    [Theory]
    [InlineData("backend/services/CardVault/src/CardVault.Api/appsettings.json")]
    [InlineData("backend/services/CardVault/src/CardVault.Api/appsettings.Development.json")]
    public void Committed_configuration_does_not_exceed_the_refresh_token_policy_default(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));

        if (!doc.RootElement.TryGetProperty("Jwt", out var jwt) ||
            !jwt.TryGetProperty("RefreshTokenDays", out var days))
        {
            return; // absent → policy default applies
        }

        days.GetInt32().Should().BeInRange(1, RefreshTokenPolicy.DefaultLifetimeDays,
            because: $"{relativePath} must not widen the refresh-token window beyond the policy default of {RefreshTokenPolicy.DefaultLifetimeDays} days");
    }
}
