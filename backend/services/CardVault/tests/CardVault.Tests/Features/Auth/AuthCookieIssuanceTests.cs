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
/// SEC-03 (task 4.1): a successful login must set HttpOnly/Secure/SameSite `cv_at` and
/// `cv_rt` cookies, and the JSON body must no longer carry a JS-readable accessToken/
/// refreshToken field. RED before the AuthCookieWriter + controller changes exist:
/// the current handler returns the tokens directly in the body and sets no cookies.
/// </summary>
[Collection("WebApp")]
public sealed class AuthCookieIssuanceTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly CardVaultWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthCookieIssuanceTests(CardVaultWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedUserAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
            return;

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, password);
        result.Succeeded.Should().BeTrue("test user must be created for the cookie-login scenario");
    }

    private static string[] GetSetCookieHeaders(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToArray() : Array.Empty<string>();

    [Fact]
    public async Task Login_Success_SetsHttpOnlySecureSameSiteCookies()
    {
        const string email = "cookie-issuance@demo.com";
        const string password = "CookieTest123!";
        await SeedUserAsync(email, password);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookies = GetSetCookieHeaders(response);
        setCookies.Should().NotBeEmpty("login must issue Set-Cookie headers");

        var accessCookie = setCookies.FirstOrDefault(c => c.StartsWith("cv_at=", StringComparison.OrdinalIgnoreCase));
        var refreshCookie = setCookies.FirstOrDefault(c => c.StartsWith("cv_rt=", StringComparison.OrdinalIgnoreCase));

        accessCookie.Should().NotBeNull("an access-token cookie named cv_at must be set");
        refreshCookie.Should().NotBeNull("a refresh-token cookie named cv_rt must be set");

        foreach (var cookie in new[] { accessCookie!, refreshCookie! })
        {
            cookie.Should().Contain("httponly", "token cookies must be HttpOnly");
            cookie.Should().Contain("secure", "token cookies must be Secure");
            cookie.Should().Contain("samesite", "token cookies must carry a SameSite attribute");
        }
    }

    [Fact]
    public async Task Login_Success_BodyDoesNotContainJsReadableTokens()
    {
        const string email = "cookie-issuance-body@demo.com";
        const string password = "CookieTest123!";
        await SeedUserAsync(email, password);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("accessToken", "the raw access token must not be JS-readable in the response body");
        body.Should().NotContain("refreshToken", "the raw refresh token must not be JS-readable in the response body");
    }
}
