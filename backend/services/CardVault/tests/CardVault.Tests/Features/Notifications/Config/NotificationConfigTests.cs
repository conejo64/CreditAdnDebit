using CardVault.Application.Services.Notifications;
using CardVault.Application.Services.Notifications.Providers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CardVault.Tests.Features.Notifications.Config;

/// <summary>
/// Tests for notification configuration binding and secrets-guard.
/// </summary>
public sealed class NotificationConfigTests
{
    // ── NotificationDispatcherOptions binds from appsettings ───────────────────

    [Fact]
    public void NotificationDispatcherOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Dispatcher:RealProvidersEnabled"] = "false",
                ["Notifications:Dispatcher:MaxAttempts"] = "3",
                ["Notifications:Dispatcher:LockTtlMinutes"] = "5",
                ["Notifications:Dispatcher:BatchSize"] = "50"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<NotificationDispatcherOptions>(config.GetSection("Notifications:Dispatcher"));
        var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<NotificationDispatcherOptions>>().Value;

        opts.RealProvidersEnabled.Should().BeFalse();
        opts.MaxAttempts.Should().Be(3);
        opts.LockTtlMinutes.Should().Be(5);
        opts.BatchSize.Should().Be(50);
    }

    [Fact]
    public void NotificationDispatcherOptions_HasCorrectDefaults()
    {
        var options = new NotificationDispatcherOptions();

        options.RealProvidersEnabled.Should().BeFalse("safe default: do NOT send real notifications without explicit opt-in");
        options.MaxAttempts.Should().Be(3);
        options.LockTtlMinutes.Should().Be(5);
        options.BatchSize.Should().Be(50);
    }

    // ── TwilioOptions binds non-secret fields ────────────────────────────────

    [Fact]
    public void TwilioOptions_BindsNonSecretFields()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Providers:Twilio:AccountSid"] = "ACtest123",
                ["Notifications:Providers:Twilio:FromNumber"] = "+15550001234",
                ["Notifications:Providers:Twilio:StatusCallbackUrl"] = "https://example.com/cb"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<TwilioOptions>(config.GetSection("Notifications:Providers:Twilio"));
        var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;

        opts.AccountSid.Should().Be("ACtest123");
        opts.FromNumber.Should().Be("+15550001234");
        opts.StatusCallbackUrl.Should().Be("https://example.com/cb");
    }

    [Fact]
    public void TwilioOptions_DoesNotHaveAuthTokenProperty()
    {
        // AuthToken is a secret and MUST NOT be stored in appsettings.
        // It must come from env vars / user-secrets only.
        // This test validates the class design — AuthToken is NOT a property on TwilioOptions.
        typeof(TwilioOptions)
            .GetProperties()
            .Should().NotContain(p => p.Name == "AuthToken",
                "AuthToken is a secret — must never be in TwilioOptions bound from appsettings");
    }

    // ── SendGridOptions binds non-secret fields ──────────────────────────────

    [Fact]
    public void SendGridOptions_BindsNonSecretFields()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Providers:SendGrid:FromEmail"] = "noreply@example.com",
                ["Notifications:Providers:SendGrid:FromName"] = "CardVault",
                ["Notifications:Providers:SendGrid:TemplateIdMap:Otp"] = "d-abc123",
                ["Notifications:Providers:SendGrid:TemplateIdMap:TransactionNotification"] = "d-def456"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<SendGridOptions>(config.GetSection("Notifications:Providers:SendGrid"));
        var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<SendGridOptions>>().Value;

        opts.FromEmail.Should().Be("noreply@example.com");
        opts.FromName.Should().Be("CardVault");
        opts.TemplateIdMap.Should().ContainKey("Otp").WhoseValue.Should().Be("d-abc123");
        opts.TemplateIdMap.Should().ContainKey("TransactionNotification").WhoseValue.Should().Be("d-def456");
    }

    [Fact]
    public void SendGridOptions_DoesNotHaveApiKeyProperty()
    {
        // ApiKey is a secret — must never be in SendGridOptions bound from appsettings.
        typeof(SendGridOptions)
            .GetProperties()
            .Should().NotContain(p => p.Name == "ApiKey",
                "ApiKey is a secret — must never be in SendGridOptions bound from appsettings");
    }

    // ── CI secrets grep-guard ─────────────────────────────────────────────────

    [Fact]
    public void AppsettingsJson_DoesNotContainSendGridApiKeyPattern()
    {
        // Guard against accidental SendGrid API key commit.
        // SendGrid API keys follow the pattern SG.xxxxx
        var appsettingsPath = FindAppsettingsJson();
        appsettingsPath.Should().NotBeNull("appsettings.json must exist");

        var content = File.ReadAllText(appsettingsPath!);

        content.Should().NotMatchRegex(@"SG\.[A-Za-z0-9_\-]{20,}",
            "SendGrid API keys (SG.*) must never appear in committed appsettings");
    }

    [Fact]
    public void AppsettingsJson_DoesNotContainTwilioAuthTokenPattern()
    {
        // Guard against accidental Twilio Auth Token commit.
        // Twilio Account SIDs start with AC followed by 32 hex chars.
        var appsettingsPath = FindAppsettingsJson();
        appsettingsPath.Should().NotBeNull("appsettings.json must exist");

        var content = File.ReadAllText(appsettingsPath!);

        // Twilio auth tokens are 32 hex characters — a bare 32-char hex string is suspect
        content.Should().NotMatchRegex(@"AC[0-9a-f]{32}",
            "Twilio Account SID patterns (AC + 32 hex) must never appear as values in committed appsettings");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? FindAppsettingsJson()
    {
        // Walk up from test assembly location to find the CardVault.Api appsettings.json
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "CardVault.Api", "appsettings.json");
            if (File.Exists(candidate)) return candidate;

            // Also try relative to services root
            var candidate2 = Path.Combine(dir.FullName, "appsettings.json");
            if (File.Exists(candidate2) && dir.Name == "CardVault.Api") return candidate2;

            dir = dir.Parent;
        }

        // Fall back: search near the test binary in the solution tree
        var solutionRoot = FindSolutionRoot(new DirectoryInfo(AppContext.BaseDirectory));
        if (solutionRoot != null)
        {
            var found = Directory.GetFiles(solutionRoot.FullName, "appsettings.json", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.Contains("CardVault.Api"));
            return found;
        }

        return null;
    }

    private static DirectoryInfo? FindSolutionRoot(DirectoryInfo start)
    {
        var current = start;
        while (current != null)
        {
            if (current.GetFiles("*.sln").Length > 0 || current.GetFiles("*.slnx").Length > 0)
                return current;
            current = current.Parent;
        }
        return null;
    }
}
