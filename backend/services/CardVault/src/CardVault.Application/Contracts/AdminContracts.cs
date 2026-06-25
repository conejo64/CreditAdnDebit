namespace CardVault.Application.Contracts;

public sealed record AdminUserResponse(
    string Id,
    string Email,
    string Name,
    string PrimaryRole,
    string[] Roles,
    string[] Permissions,
    string Status,
    DateTimeOffset? LastActivityOn);

public sealed record AdminRoleSummaryResponse(
    string Name,
    string Description,
    int UsersCount);

public sealed record AdminRoleDetailResponse(
    string Name,
    string Description,
    bool IsCanonical,
    int UsersCount,
    string[] Permissions);

public sealed record AdminPermissionEntry(
    string Value,
    string Description);

public sealed record CreateAdminUserRequest(
    string Email,
    string Password,
    string[] Roles);

public sealed record UpdateUserRolesRequest(
    string[] Roles);

public sealed record CreateRoleRequest(
    string Name,
    string Description);

public sealed record SetRolePermissionsRequest(
    string[] Permissions);

public sealed record SetUserPermissionsRequest(
    string[] Permissions);

public sealed record ResetPasswordRequest(
    string NewPassword);
