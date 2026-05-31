namespace CardVault.Api.Contracts;

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthenticatedUserResponse(
    string Id,
    string Email,
    string Name,
    string PrimaryRole,
    string[] Roles,
    string[] Permissions);

public sealed record AuthSessionResponse(
    bool MfaRequired,
    string? AccessToken,
    string? RefreshToken,
    string? Message,
    AuthenticatedUserResponse? User);

public sealed record RefreshRequest(string RefreshToken);

public sealed record MfaEnableRequest(string Email, string Password);

public sealed record MfaEnableResponse(string OtpauthUri, string ManualKey, string[] RecoveryCodes);

public sealed record MfaVerifyRequest(string Email, string Password, string Code);

public sealed record DemoPublishRequest(string Message);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordByTokenRequest(string Token, string NewPassword);
