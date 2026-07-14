using CardVault.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CardVault.Api.Security;

/// <summary>
/// SEC-03: cookie names for the HttpOnly access/refresh token cookies.
/// </summary>
public static class AuthCookieNames
{
    public const string AccessToken = "cv_at";
    public const string RefreshToken = "cv_rt";
}

/// <summary>
/// SEC-03: presentation-layer helper that turns a MediatR-produced
/// <see cref="AuthSessionResponse"/> body into HttpOnly/Secure cookies, stripping the raw
/// token material out of the JSON response. This is intentionally the ONLY place that
/// writes auth cookies — <see cref="CardVault.Application"/> handlers stay pure and never
/// see <see cref="HttpContext"/>/<see cref="HttpResponse"/>.
///
/// `Secure=true` and `HttpOnly=true` are unconditional here — there is no environment
/// branch that can relax them, satisfying the "Production never relaxes HttpOnly or
/// Secure" contract by construction (task 4.4).
/// </summary>
public static class AuthCookieWriter
{
    public static CookieOptions BuildAccessTokenCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax
    };

    public static CookieOptions BuildRefreshTokenCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/api/auth"
    };

    /// <summary>
    /// If <paramref name="result"/> is a successful <see cref="AuthSessionResponse"/>
    /// carrying token material, writes the cv_at/cv_rt cookies and returns a stripped body
    /// (mfaRequired/message/user only). Any other result (errors, MFA-required-without-
    /// tokens) is passed through unchanged.
    /// </summary>
    public static IResult ApplyCookies(HttpContext context, IResult result)
    {
        if (result is not Ok<AuthSessionResponse> ok || ok.Value is not { } session)
            return result;

        if (!string.IsNullOrEmpty(session.AccessToken) && !string.IsNullOrEmpty(session.RefreshToken))
        {
            context.Response.Cookies.Append(AuthCookieNames.AccessToken, session.AccessToken, BuildAccessTokenCookieOptions());
            context.Response.Cookies.Append(AuthCookieNames.RefreshToken, session.RefreshToken, BuildRefreshTokenCookieOptions());
        }

        return Results.Ok(new
        {
            mfaRequired = session.MfaRequired,
            message = session.Message,
            user = session.User
        });
    }

    /// <summary>
    /// Clears both token cookies (logout). Path must match the path each cookie was
    /// issued with, otherwise the browser will not recognize it as the same cookie.
    /// </summary>
    public static void ClearCookies(HttpResponse response)
    {
        response.Cookies.Delete(AuthCookieNames.AccessToken, new CookieOptions { Path = "/" });
        response.Cookies.Delete(AuthCookieNames.RefreshToken, new CookieOptions { Path = "/api/auth" });
    }
}
