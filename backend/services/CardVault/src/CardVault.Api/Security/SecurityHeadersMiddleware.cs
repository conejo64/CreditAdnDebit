using Microsoft.AspNetCore.Http;

namespace CardVault.Api.Security;

/// <summary>
/// SEC-12: emits baseline security response headers on every CardVault response.
/// The CSP intentionally allows 'unsafe-inline' script/style so Swagger UI (which runs
/// unconditionally, not gated to Development) keeps working in every environment,
/// while still denying framing via `frame-ancestors 'none'` (CSP-native equivalent of
/// `X-Frame-Options: DENY`).
/// </summary>
public sealed class SecurityHeadersMiddleware : IMiddleware
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;

        await next(context);
    }
}
