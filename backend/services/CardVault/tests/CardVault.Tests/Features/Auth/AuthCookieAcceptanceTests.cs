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
/// SEC-03 (task 4.5): the JWT-bearer pipeline must accept the access token from the
/// `cv_at` cookie when no Authorization header is present, and authorize identically to
/// an equivalent bearer-token caller. RED before the `OnMessageReceived` cookie-fallback
/// event is wired into `AddJwtBearer` in Program.cs.
/// </summary>
[Collection("WebApp")]
public sealed class AuthCookieAcceptanceTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly CardVaultWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthCookieAcceptanceTests(CardVaultWebApplicationFactory factory)
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
        var accessCookie = ExtractCookieValue(setCookies, "cv_at");
        var refreshCookie = ExtractCookieValue(setCookies, "cv_rt");
        return (accessCookie, refreshCookie);
    }

    private static string ExtractCookieValue(IEnumerable<string> setCookieHeaders, string name)
    {
        var header = setCookieHeaders.First(c => c.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase));
        var nameValuePart = header.Split(';')[0];
        return nameValuePart[(name.Length + 1)..];
    }

    [Fact]
    public async Task ProtectedEndpoint_AcceptsAccessTokenFromCookie_NoAuthorizationHeader()
    {
        var (accessTokenCookieValue, _) = await LoginAndCaptureCookiesAsync(
            "cookie-acceptance@demo.com", "CookieAcceptance123!");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", $"cv_at={accessTokenCookieValue}");
        // Explicitly no Authorization header.

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "a valid cv_at cookie must authorize the request exactly like an equivalent bearer token");
    }

    [Fact]
    public async Task ProtectedEndpoint_NoCookieNoHeader_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
