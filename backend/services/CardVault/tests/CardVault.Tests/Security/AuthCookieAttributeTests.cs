using CardVault.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace CardVault.Tests.Security;

/// <summary>
/// SEC-03 (task 4.4): the access/refresh token cookie options must always carry
/// HttpOnly=true and Secure=true, with no environment-conditional branching that could
/// relax them in Production. RED before AuthCookieWriter exists.
/// </summary>
public sealed class AuthCookieAttributeTests
{
    [Fact]
    public void AccessTokenCookie_IsAlwaysHttpOnlyAndSecure()
    {
        var options = AuthCookieWriter.BuildAccessTokenCookieOptions();

        options.HttpOnly.Should().BeTrue("access-token cookie must never be readable by JS");
        options.Secure.Should().BeTrue("access-token cookie must never be relaxed, even outside Production");
        options.SameSite.Should().Be(SameSiteMode.Lax);
    }

    [Fact]
    public void RefreshTokenCookie_IsAlwaysHttpOnlyAndSecure_AndScopedToAuthPath()
    {
        var options = AuthCookieWriter.BuildRefreshTokenCookieOptions();

        options.HttpOnly.Should().BeTrue("refresh-token cookie must never be readable by JS");
        options.Secure.Should().BeTrue("refresh-token cookie must never be relaxed, even outside Production");
        options.SameSite.Should().Be(SameSiteMode.Lax);
        options.Path.Should().Be("/api/auth", "the refresh cookie must be scoped only to auth endpoints");
    }
}
