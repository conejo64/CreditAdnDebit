using System.Net;
using System.Net.Http.Json;
using CardVault.Application.Contracts;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CardVault.Tests.Features.Auth;

/// <summary>
/// G1-T1: fixed-window rate limiting on the anonymous authentication endpoints.
/// Policies: auth_login (login), auth_mfa (mfa/enable, mfa/verify), auth_refresh (refresh),
/// each partitioned by client IP. Defaults are 10/min, 10/min and 30/min; this test lowers
/// them through configuration (Auth:RateLimits:{Login|Mfa|Refresh}:PermitLimit) so the
/// 429 path is exercised with a handful of requests. TestServer reports no remote IP, so
/// every request here lands in the same partition.
/// </summary>
public sealed class AuthRateLimitHttpTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AuthRateLimitHttpTests()
    {
        _factory = new CardVaultWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Auth:RateLimits:Login:PermitLimit", "3");
                b.UseSetting("Auth:RateLimits:Mfa:PermitLimit", "2");
                b.UseSetting("Auth:RateLimits:Refresh:PermitLimit", "2");
            });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Login_ExceedingTheWindowLimit_Returns429()
    {
        for (var i = 0; i < 3; i++)
        {
            var allowed = await _client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest($"ratelimit-{i}@demo.com", "Whatever1234!"));
            allowed.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "requests inside the window reach the handler (unknown user -> 401)");
        }

        var rejected = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("ratelimit-4@demo.com", "Whatever1234!"));

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Refresh_ExceedingTheWindowLimit_Returns429()
    {
        for (var i = 0; i < 2; i++)
        {
            var allowed = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest("not-a-token"));
            allowed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var rejected = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest("not-a-token"));

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task MfaEnableAndVerify_ShareOneWindow_AndReturn429WhenExceeded()
    {
        var enable = await _client.PostAsJsonAsync("/api/auth/mfa/enable",
            new MfaEnableRequest("mfa-ratelimit@demo.com", "Whatever1234!"));
        enable.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var verify = await _client.PostAsJsonAsync("/api/auth/mfa/verify",
            new MfaVerifyRequest("mfa-ratelimit@demo.com", "Whatever1234!", "000000"));
        verify.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var rejected = await _client.PostAsJsonAsync("/api/auth/mfa/enable",
            new MfaEnableRequest("mfa-ratelimit@demo.com", "Whatever1234!"));

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "mfa/enable and mfa/verify share the auth_mfa partition");
    }
}
