namespace CardVault.Application.Contracts;

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthenticatedUserResponse(
    string Id,
    string Email,
    string Name,
    string PrimaryRole,
    string[] Roles,
    string[] Permissions);

/// <summary>
/// Session envelope produced by login / MFA verify / refresh. When <see cref="MfaRequired"/>
/// is true no token material is present; <see cref="MfaPendingToken"/> carries the
/// short-lived, single-use challenge that <c>mfa/verify</c> consumes (G1-T3).
/// </summary>
public sealed record AuthSessionResponse(
    bool MfaRequired,
    string? AccessToken,
    string? RefreshToken,
    string? Message,
    AuthenticatedUserResponse? User,
    string? MfaPendingToken = null);

public sealed record RefreshRequest(string? RefreshToken = null);

/// <summary>
/// G1-T3: enrolment operates on the authenticated caller; the body carries only the fresh
/// password re-check. Any e-mail in the body is ignored.
/// </summary>
public sealed record MfaEnableRequest(string Password);

public sealed record MfaEnableResponse(string OtpauthUri, string ManualKey, string[] RecoveryCodes);

/// <summary>
/// G1-T3: the second step of login. <see cref="MfaPendingToken"/> is the token returned by
/// the login step; e-mail and password are no longer accepted here.
/// </summary>
public sealed record MfaVerifyRequest(string MfaPendingToken, string Code);

public sealed record DemoPublishRequest(string Message);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordByTokenRequest(string Token, string NewPassword);
