using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace CardVault.Api.Security;

/// <summary>
/// SEC-12: emits baseline security response headers on every CardVault response.
/// The CSP denies framing via `frame-ancestors 'none'` (CSP-native equivalent of
/// `X-Frame-Options: DENY`).
///
/// Outside Development the policy is strict — no 'unsafe-inline', so the CSP keeps its
/// XSS mitigation intact. The 'unsafe-inline' relaxation exists only in Development, where
/// Swagger UI (gated to Development in Program.cs) needs inline script/style to render.
/// Production never serves the relaxed policy.
/// </summary>
public sealed class SecurityHeadersMiddleware : IMiddleware
{
    private const string StrictContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self'; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'";

    private const string DevelopmentContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'";

    private readonly string _contentSecurityPolicy;

    public SecurityHeadersMiddleware(IHostEnvironment environment)
    {
        _contentSecurityPolicy = environment.IsDevelopment()
            ? DevelopmentContentSecurityPolicy
            : StrictContentSecurityPolicy;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] = _contentSecurityPolicy;

        await next(context);
    }
}
