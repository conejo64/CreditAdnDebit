using System.Net;
using System.Net.Http.Json;
using CardVault.Application.Contracts;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace CardVault.Tests.Features.Auth;

/// <summary>
/// G1-T1 (PCI DSS v4 8.3.4): repeated failed logins must lock the account through
/// ASP.NET Identity lockout, and the login response must stay indistinguishable for an
/// unknown user, a wrong password and a locked account (no account enumeration).
///
/// Uses a dedicated factory with a very high login rate limit so the lockout state
/// machine (11+ requests from the same client) is observed without the 429 policy
/// interfering; the rate-limit policy itself is covered by <see cref="AuthRateLimitHttpTests"/>.
/// </summary>
public sealed class AuthLockoutHttpTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AuthLockoutHttpTests()
    {
        _factory = new CardVaultWebApplicationFactory()
            .WithWebHostBuilder(b => b.UseSetting("Auth:RateLimits:Login:PermitLimit", "10000"));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task SeedUserAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, password);
        result.Succeeded.Should().BeTrue("test user must be created: {0}",
            string.Join(",", result.Errors.Select(e => e.Description)));
    }

    private async Task<AppUser> ReloadAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return (await userManager.FindByEmailAsync(email))!;
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password)
        => _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

    [Fact]
    public async Task TenFailedLogins_LockTheAccount_AndCorrectPasswordIsRejectedWhileLocked()
    {
        const string email = "lockout-ten@demo.com";
        const string password = "LockoutTestPassword1!";
        await SeedUserAsync(email, password);

        for (var i = 0; i < 10; i++)
        {
            var failed = await LoginAsync(email, "WrongPassword-" + i);
            failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var locked = await ReloadAsync(email);
        locked.LockoutEnd.Should().NotBeNull("the tenth failure must set LockoutEnd");
        locked.LockoutEnd!.Value.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(25),
            "the default lockout window is 30 minutes");
        locked.LockoutEnd.Value.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(35));

        var correctWhileLocked = await LoginAsync(email, password);
        correctWhileLocked.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a correct password during lockout must still be rejected");
    }

    [Fact]
    public async Task FewerThanTenFailures_DoNotLock_AndSuccessResetsTheCounter()
    {
        const string email = "lockout-reset@demo.com";
        const string password = "LockoutTestPassword2!";
        await SeedUserAsync(email, password);

        for (var i = 0; i < 3; i++)
            (await LoginAsync(email, "WrongPassword")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var afterFailures = await ReloadAsync(email);
        afterFailures.AccessFailedCount.Should().Be(3, "every wrong password must be counted");
        afterFailures.LockoutEnd.Should().BeNull();

        var success = await LoginAsync(email, password);
        success.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterSuccess = await ReloadAsync(email);
        afterSuccess.AccessFailedCount.Should().Be(0, "a successful login resets the failure counter");
    }

    [Fact]
    public async Task UnknownUser_WrongPassword_AndLockedAccount_ReturnIdenticalResponses()
    {
        const string email = "lockout-indistinguishable@demo.com";
        const string password = "LockoutTestPassword3!";
        await SeedUserAsync(email, password);

        var unknown = await LoginAsync("nobody-" + Guid.NewGuid().ToString("N") + "@demo.com", "Whatever1234!");
        var wrong = await LoginAsync(email, "WrongPassword");

        for (var i = 0; i < 10; i++)
            await LoginAsync(email, "WrongPassword");
        var locked = await LoginAsync(email, password);

        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var wrongBody = await wrong.Content.ReadAsStringAsync();
        var lockedBody = await locked.Content.ReadAsStringAsync();

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrong.StatusCode.Should().Be(unknown.StatusCode);
        locked.StatusCode.Should().Be(unknown.StatusCode);
        wrongBody.Should().Be(unknownBody, "wrong password must not be distinguishable from unknown user");
        lockedBody.Should().Be(unknownBody, "a locked account must not be disclosed to the caller");
        lockedBody.Should().NotContainEquivalentOf("lock");
    }
}
