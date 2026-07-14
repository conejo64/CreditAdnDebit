using System.Net;
using System.Net.Http.Json;
using CardVault.Application.Contracts;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace CardVault.Tests.Features.Auth;

/// <summary>
/// SEC-03 (tasks 4.7, 4.9): refresh reissues cookies from the `cv_rt` cookie with an
/// empty/absent body, and logout clears both cookies + rejects a subsequent protected
/// call. RED before the controller reads the cookie / the logout endpoint exists.
/// </summary>
[Collection("WebApp")]
public sealed class AuthRefreshLogoutCookieTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly CardVaultWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthRefreshLogoutCookieTests(CardVaultWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(string accessTokenCookieValue, string refreshTokenCookieValue)> LoginAndCaptureCookiesAsync(
        string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await userManager.FindByEmailAsync(email) is null)
        {
            var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
            (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        }

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookies = response.Headers.GetValues("Set-Cookie").ToArray();
        return (ExtractCookieValue(setCookies, "cv_at"), ExtractCookieValue(setCookies, "cv_rt"));
    }

    private static string ExtractCookieValue(IEnumerable<string> setCookieHeaders, string name)
    {
        var header = setCookieHeaders.First(c => c.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase));
        var nameValuePart = header.Split(';')[0];
        return nameValuePart[(name.Length + 1)..];
    }

    [Fact]
    public async Task Refresh_WithRefreshCookieAndNoBody_ReissuesBothCookies()
    {
        var (_, refreshTokenCookieValue) = await LoginAndCaptureCookiesAsync(
            "refresh-cookie@demo.com", "RefreshCookie123!");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"cv_rt={refreshTokenCookieValue}");
        // No Content set at all — proves the body is not required.

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "a valid cv_rt cookie must be sufficient without any request body");

        var setCookies = response.Headers.GetValues("Set-Cookie").ToArray();
        setCookies.Should().Contain(c => c.StartsWith("cv_at=", StringComparison.OrdinalIgnoreCase));
        setCookies.Should().Contain(c => c.StartsWith("cv_rt=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Refresh_NoBodyNoCookie_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ClearsBothCookies_AndSubsequentProtectedCallIsRejected()
    {
        var (accessTokenCookieValue, refreshTokenCookieValue) = await LoginAndCaptureCookiesAsync(
            "logout-cookie@demo.com", "LogoutCookie123!");

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("Cookie", $"cv_at={accessTokenCookieValue}; cv_rt={refreshTokenCookieValue}");

        var logoutResponse = await _client.SendAsync(logoutRequest);
        logoutResponse.IsSuccessStatusCode.Should().BeTrue();

        var setCookies = logoutResponse.Headers.GetValues("Set-Cookie").ToArray();
        setCookies.Should().Contain(c => c.StartsWith("cv_at=", StringComparison.OrdinalIgnoreCase) &&
            (c.Contains("expires=", StringComparison.OrdinalIgnoreCase) || c.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)));
        setCookies.Should().Contain(c => c.StartsWith("cv_rt=", StringComparison.OrdinalIgnoreCase) &&
            (c.Contains("expires=", StringComparison.OrdinalIgnoreCase) || c.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)));

        // Simulate a client that honored the cookie deletion: do NOT resend cookies.
        using var protectedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        var protectedResponse = await _client.SendAsync(protectedRequest);

        protectedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesStoredRefreshToken_SoItCanNoLongerBeUsedToRefresh()
    {
        var (_, refreshTokenCookieValue) = await LoginAndCaptureCookiesAsync(
            "logout-revoke@demo.com", "LogoutRevoke123!");

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("Cookie", $"cv_rt={refreshTokenCookieValue}");
        (await _client.SendAsync(logoutRequest)).IsSuccessStatusCode.Should().BeTrue();

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", $"cv_rt={refreshTokenCookieValue}");

        var refreshResponse = await _client.SendAsync(refreshRequest);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "logout must revoke the stored refresh token, not just clear the cookie client-side");
    }
}
