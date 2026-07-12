using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace CardVault.Tests.Security;

/// <summary>
/// TDD tests (RED before SEC-01 config purge): verifies the committed
/// appsettings.Development.json files for CardVault and IsoSwitch contain no
/// live secret material, satisfying security-hardening SEC-9 scenarios
/// "Committed development config contains no live vault key" and
/// "Committed config contains no inline connection-string password".
/// Reads the raw JSON file from disk — this is a config-shape contract test,
/// not a runtime binding test.
/// </summary>
public sealed class CommittedConfigSecretShapeTests
{
    // Base64 of exactly 32 raw bytes (AES-256 key length): 43 chars + '=' padding, no '/' required.
    private static readonly Regex Base64Aes256KeyPattern =
        new(@"^[A-Za-z0-9+/]{43}=$", RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        var current = new DirectoryInfo(dir);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "backend")))
            current = current.Parent;

        if (current is null)
            throw new InvalidOperationException("Could not locate repository root from test base directory.");

        return current.FullName;
    }

    private static JsonElement LoadJson(string relativePathFromRepoRoot)
    {
        var repoRoot = FindRepoRoot();
        var fullPath = Path.Combine(repoRoot, relativePathFromRepoRoot);
        File.Exists(fullPath).Should().BeTrue(because: $"the committed config file must exist at {fullPath}");
        var json = File.ReadAllText(fullPath);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // ─── CardVault: no live vault key under Vault:Keys ────────────────────────

    [Fact]
    public void CardVaultDevelopmentConfig_VaultKeys_ContainsNoBase64Aes256KeyValue()
    {
        var root = LoadJson("backend/services/CardVault/src/CardVault.Api/appsettings.Development.json");

        if (!root.TryGetProperty("Vault", out var vault) || !vault.TryGetProperty("Keys", out var keys))
            return; // no Vault:Keys section at all satisfies "no live key" trivially

        foreach (var prop in keys.EnumerateObject())
        {
            var value = prop.Value.GetString() ?? string.Empty;
            Base64Aes256KeyPattern.IsMatch(value).Should().BeFalse(
                because: $"Vault:Keys:{prop.Name} must not contain a live Base64 AES-256 key in committed config");
        }
    }

    [Fact]
    public void CardVaultDevelopmentConfig_VaultKeys_DoesNotContainLeakedK1OrK2Values()
    {
        var root = LoadJson("backend/services/CardVault/src/CardVault.Api/appsettings.Development.json");
        var json = root.GetRawText();

        json.Should().NotContain("G64aK3Q44+yrGd5Mjgkq2D/4TDedO3dRwzjIOHsa11Q=",
            because: "the previously leaked k1 value must be purged from committed config");
        json.Should().NotContain("4cnKCNXOU7qpb4JEVUW/FBmRdW9azcXIK/tkOj/gOaY=",
            because: "the previously leaked k2 value must be purged from committed config");
    }

    // ─── No inline connection-string password in any committed appsettings*.json ──

    [Theory]
    [InlineData("backend/services/CardVault/src/CardVault.Api/appsettings.Development.json")]
    [InlineData("backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.Development.json")]
    public void CommittedConfig_ConnectionStrings_ContainNoInlinePassword(string relativePath)
    {
        var root = LoadJson(relativePath);

        if (!root.TryGetProperty("ConnectionStrings", out var connectionStrings))
            return;

        foreach (var prop in connectionStrings.EnumerateObject())
        {
            var value = prop.Value.GetString() ?? string.Empty;
            value.Should().NotContain("Password=",
                because: $"ConnectionStrings:{prop.Name} must not contain an inline password in committed config");
        }
    }

    // ─── No seed credential keys present ──────────────────────────────────────

    [Fact]
    public void CardVaultDevelopmentConfig_Seed_ContainsNoAdminCredentialValues()
    {
        var root = LoadJson("backend/services/CardVault/src/CardVault.Api/appsettings.Development.json");

        if (!root.TryGetProperty("Seed", out var seed))
            return;

        if (seed.TryGetProperty("AdminEmail", out var adminEmail))
            adminEmail.GetString().Should().BeNullOrEmpty(
                because: "Seed:AdminEmail must not be a live committed credential");

        if (seed.TryGetProperty("AdminPassword", out var adminPassword))
            adminPassword.GetString().Should().BeNullOrEmpty(
                because: "Seed:AdminPassword must not be a live committed credential");

        if (seed.TryGetProperty("OpenBankingClientSecret", out var obSecret))
            obSecret.GetString().Should().BeNullOrEmpty(
                because: "Seed:OpenBankingClientSecret must not be a live committed credential");
    }

    // ─── IsoSwitch: no dev-admin-key placeholder ──────────────────────────────

    [Fact]
    public void IsoSwitchDevelopmentConfig_AdminApiKey_DoesNotContainDevPlaceholder()
    {
        var root = LoadJson("backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.Development.json");

        if (!root.TryGetProperty("Admin", out var admin) || !admin.TryGetProperty("ApiKey", out var apiKey))
            return;

        apiKey.GetString().Should().NotBe("dev-admin-key",
            because: "the dev-admin-key literal must be purged from committed config (SEC-11)");
    }
}
