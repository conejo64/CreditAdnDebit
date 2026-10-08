using System.Diagnostics;
using CardVault.Application.Contracts;
using CardVault.Application.Services;
using CardVault.Api.Security;
using CardVault.Infrastructure.Identity.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "CanManageUsersRoles")]
public class AdminController : ControllerBase
{
    private static readonly string[] CanonicalRoles = ["Admin", "Operator", "Auditor"];

    private readonly UserManager<AppUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly TokenService _tokens;
    private readonly AuditService _audit;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        UserManager<AppUser> users,
        RoleManager<IdentityRole> roles,
        TokenService tokens,
        AuditService audit,
        ILogger<AdminController> logger)
    {
        _users = users;
        _roles = roles;
        _tokens = tokens;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// G1-T6: stable audit event types, one per administrative mutation. A failed attempt
    /// reuses the same type with outcome "failed" and a machine-readable reason.
    /// </summary>
    public static class AuditEvents
    {
        public const string UserCreated            = "admin.user.created";
        public const string UserDeleted            = "admin.user.deleted";
        public const string UserBlocked            = "admin.user.blocked";
        public const string UserUnblocked          = "admin.user.unblocked";
        public const string UserPasswordReset      = "admin.user.password_reset";
        public const string UserRolesChanged       = "admin.user.roles_changed";
        public const string UserPermissionsChanged = "admin.user.permissions_changed";
        public const string RoleCreated            = "admin.role.created";
        public const string RoleDeleted            = "admin.role.deleted";
        public const string RolePermissionsChanged = "admin.role.permissions_changed";
    }

    // ──────────────────────────────────────────────────────────────────
    // PERMISSIONS CATALOG
    // ──────────────────────────────────────────────────────────────────

    [HttpGet("permissions")]
    public ActionResult<IReadOnlyList<AdminPermissionEntry>> GetPermissions()
    {
        var result = PermissionCatalog.All
            .Select(p => new AdminPermissionEntry(p,
                PermissionCatalog.Descriptions.TryGetValue(p, out var desc) ? desc : p))
            .ToList();
        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────────────
    // USERS
    // ──────────────────────────────────────────────────────────────────

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<AdminUserResponse>>> GetUsers(CancellationToken ct)
    {
        var users = await _users.Users
            .OrderBy(x => x.Email)
            .ToListAsync(ct);

        var responses = new List<AdminUserResponse>(users.Count);
        foreach (var user in users)
            responses.Add(await BuildAdminUserResponseAsync(user));

        return Ok(responses);
    }

    [HttpPost("users")]
    public async Task<ActionResult<AdminUserResponse>> CreateUser([FromBody] CreateAdminUserRequest request)
    {
        // Never the password: only who was requested and with which roles.
        var requestedTarget = UserTarget(userId: null, request.Email);
        var requestedChange = new { roles = request.Roles ?? [] };

        var (validatedRoles, roleError) = await ValidateRolesAsync(request.Roles);
        if (roleError is not null)
        {
            await AuditFailedAsync(AuditEvents.UserCreated, "unknown_role", requestedTarget, requestedChange);
            return BadRequest(new { message = roleError });
        }

        if (validatedRoles.Count == 0)
        {
            await AuditFailedAsync(AuditEvents.UserCreated, "roles_required", requestedTarget, requestedChange);
            return BadRequest(new { message = "Al menos un rol es requerido." });
        }

        var existing = await _users.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            await AuditFailedAsync(AuditEvents.UserCreated, "user_already_exists",
                UserTarget(existing.Id, request.Email), requestedChange);
            return Conflict(new { message = "El usuario ya existe." });
        }

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true
        };

        var created = await _users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            await AuditFailedAsync(AuditEvents.UserCreated, IdentityReason(created), requestedTarget, requestedChange);
            return BadRequest(created.Errors);
        }

        var assignedRoles = new { roles = validatedRoles.OrderBy(r => r).ToArray() };
        var roleResult = await _users.AddToRolesAsync(user, validatedRoles);
        if (!roleResult.Succeeded)
        {
            // The account exists at this point; the target id in the event says so.
            await AuditFailedAsync(AuditEvents.UserCreated, "roles_not_assigned: " + IdentityReason(roleResult),
                UserTarget(user), assignedRoles);
            return BadRequest(roleResult.Errors);
        }

        await AuditSucceededAsync(AuditEvents.UserCreated, UserTarget(user), assignedRoles);

        var response = await BuildAdminUserResponseAsync(user);
        return Created($"/api/admin/users/{user.Id}", response);
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
        {
            await AuditFailedAsync(AuditEvents.UserDeleted, "user_not_found", UserTarget(id, email: null), null);
            return NotFound(new { message = "Usuario no encontrado." });
        }

        var target = UserTarget(user);
        var change = new { rolesBefore = (await _users.GetRolesAsync(user)).OrderBy(r => r).ToArray() };

        var result = await _users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            await AuditFailedAsync(AuditEvents.UserDeleted, IdentityReason(result), target, change);
            return BadRequest(result.Errors);
        }

        await AuditSucceededAsync(AuditEvents.UserDeleted, target, change);
        return NoContent();
    }

    [HttpPost("users/{id}/block")]
    public async Task<ActionResult<AdminUserResponse>> BlockUser(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
        {
            await AuditFailedAsync(AuditEvents.UserBlocked, "user_not_found", UserTarget(id, email: null), null);
            return NotFound(new { message = "Usuario no encontrado." });
        }

        var lockoutEndBefore = user.LockoutEnd;

        // LockoutEnd en un año equivale a bloqueo indefinido operativo
        var result = await _users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(1));
        if (!result.Succeeded)
        {
            await AuditFailedAsync(AuditEvents.UserBlocked, IdentityReason(result), UserTarget(user),
                LockoutChange(lockoutEndBefore, lockoutEndBefore));
            return BadRequest(result.Errors);
        }

        var enabled = await _users.SetLockoutEnabledAsync(user, true);
        if (!enabled.Succeeded)
        {
            await AuditFailedAsync(AuditEvents.UserBlocked, "lockout_not_enabled: " + IdentityReason(enabled),
                UserTarget(user), LockoutChange(lockoutEndBefore, user.LockoutEnd));
            return BadRequest(enabled.Errors);
        }

        await AuditSucceededAsync(AuditEvents.UserBlocked, UserTarget(user),
            LockoutChange(lockoutEndBefore, user.LockoutEnd));
        return Ok(await BuildAdminUserResponseAsync(user));
    }

    [HttpPost("users/{id}/unblock")]
    public async Task<ActionResult<AdminUserResponse>> UnblockUser(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
        {
            await AuditFailedAsync(AuditEvents.UserUnblocked, "user_not_found", UserTarget(id, email: null), null);
            return NotFound(new { message = "Usuario no encontrado." });
        }

        var lockoutEndBefore = user.LockoutEnd;
        var result = await _users.SetLockoutEndDateAsync(user, null);
        if (!result.Succeeded)
        {
            await AuditFailedAsync(AuditEvents.UserUnblocked, IdentityReason(result), UserTarget(user),
                LockoutChange(lockoutEndBefore, lockoutEndBefore));
            return BadRequest(result.Errors);
        }

        await AuditSucceededAsync(AuditEvents.UserUnblocked, UserTarget(user),
            LockoutChange(lockoutEndBefore, user.LockoutEnd));
        return Ok(await BuildAdminUserResponseAsync(user));
    }

    [HttpPost("users/{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetPasswordRequest request)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
        {
            await AuditFailedAsync(AuditEvents.UserPasswordReset, "user_not_found", UserTarget(id, email: null), null);
            return NotFound(new { message = "Usuario no encontrado." });
        }

        // Neither the new password nor the reset token ever reaches the audit payload.
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            await AuditFailedAsync(AuditEvents.UserPasswordReset, IdentityReason(result), UserTarget(user), null);
            return BadRequest(result.Errors);
        }

        await AuditSucceededAsync(AuditEvents.UserPasswordReset, UserTarget(user), null);
        return NoContent();
    }

    [HttpPut("users/{id}/roles")]
    public async Task<ActionResult<AdminUserResponse>> UpdateUserRoles(string id, [FromBody] UpdateUserRolesRequest request)
    {
        var requested = new { before = (string[]?)null, after = request.Roles ?? [] };

        var (validatedRoles, roleError) = await ValidateRolesAsync(request.Roles);
        if (roleError is not null)
        {
            await AuditFailedAsync(AuditEvents.UserRolesChanged, "unknown_role", UserTarget(id, email: null), requested);
            return BadRequest(new { message = roleError });
        }

        if (validatedRoles.Count == 0)
        {
            await AuditFailedAsync(AuditEvents.UserRolesChanged, "roles_required", UserTarget(id, email: null), requested);
            return BadRequest(new { message = "Al menos un rol es requerido." });
        }

        var user = await _users.FindByIdAsync(id);
        if (user is null)
        {
            await AuditFailedAsync(AuditEvents.UserRolesChanged, "user_not_found", UserTarget(id, email: null), requested);
            return NotFound(new { message = "Usuario no encontrado." });
        }

        var currentRoles = await _users.GetRolesAsync(user);
        var change = new
        {
            before = currentRoles.OrderBy(r => r).ToArray(),
            after = validatedRoles.OrderBy(r => r).ToArray()
        };

        if (currentRoles.Count > 0)
        {
            var removeResult = await _users.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                await AuditFailedAsync(AuditEvents.UserRolesChanged, IdentityReason(removeResult), UserTarget(user), change);
                return BadRequest(removeResult.Errors);
            }
        }

        var addResult = await _users.AddToRolesAsync(user, validatedRoles);
        if (!addResult.Succeeded)
        {
            // The previous roles are already gone: "after" here is what was requested, not
            // what the user holds now. The reason prefix makes that explicit.
            await AuditFailedAsync(AuditEvents.UserRolesChanged, "previous_roles_removed: " + IdentityReason(addResult),
                UserTarget(user), change);
            return BadRequest(addResult.Errors);
        }

        await AuditSucceededAsync(AuditEvents.UserRolesChanged, UserTarget(user), change);
        return Ok(await BuildAdminUserResponseAsync(user));
    }

    [HttpGet("users/{id}/permissions")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetUserPermissions(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
            return NotFound(new { message = "Usuario no encontrado." });

        var claims = await _users.GetClaimsAsync(user);
        var perms = claims.Where(c => c.Type == "perm").Select(c => c.Value).OrderBy(x => x).ToArray();
        return Ok(perms);
    }

    [HttpPut("users/{id}/permissions")]
    public async Task<ActionResult<IReadOnlyList<string>>> SetUserPermissions(string id, [FromBody] SetUserPermissionsRequest request)
    {
        var requestedPermissions = (request.Permissions ?? []).OrderBy(x => x).ToArray();

        var user = await _users.FindByIdAsync(id);
        if (user is null)
        {
            await AuditFailedAsync(AuditEvents.UserPermissionsChanged, "user_not_found", UserTarget(id, email: null),
                new { before = (string[]?)null, after = requestedPermissions });
            return NotFound(new { message = "Usuario no encontrado." });
        }

        var currentClaims = (await _users.GetClaimsAsync(user)).Where(c => c.Type == "perm").ToList();
        var change = new
        {
            before = currentClaims.Select(c => c.Value).OrderBy(x => x).ToArray(),
            after = requestedPermissions
        };

        var invalidPerms = requestedPermissions
            .Where(p => !PermissionCatalog.All.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (invalidPerms.Count > 0)
        {
            await AuditFailedAsync(AuditEvents.UserPermissionsChanged, "unknown_permission", UserTarget(user), change);
            return BadRequest(new { message = $"Permisos no válidos: {string.Join(", ", invalidPerms)}" });
        }

        if (currentClaims.Count > 0)
        {
            var removeResult = await _users.RemoveClaimsAsync(user, currentClaims);
            if (!removeResult.Succeeded)
            {
                await AuditFailedAsync(AuditEvents.UserPermissionsChanged, IdentityReason(removeResult), UserTarget(user), change);
                return BadRequest(removeResult.Errors);
            }
        }

        if (requestedPermissions.Length > 0)
        {
            var newClaims = requestedPermissions.Select(p => new Claim("perm", p)).ToList();
            var addResult = await _users.AddClaimsAsync(user, newClaims);
            if (!addResult.Succeeded)
            {
                await AuditFailedAsync(AuditEvents.UserPermissionsChanged,
                    "previous_permissions_removed: " + IdentityReason(addResult), UserTarget(user), change);
                return BadRequest(addResult.Errors);
            }
        }

        await AuditSucceededAsync(AuditEvents.UserPermissionsChanged, UserTarget(user), change);
        return Ok(requestedPermissions);
    }

    // ──────────────────────────────────────────────────────────────────
    // ROLES
    // ──────────────────────────────────────────────────────────────────

    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<AdminRoleSummaryResponse>>> GetRoles()
    {
        var allRoles = await _roles.Roles.OrderBy(r => r.Name).ToListAsync();
        var responses = new List<AdminRoleSummaryResponse>(allRoles.Count);
        foreach (var role in allRoles)
        {
            if (role.Name is null) continue;
            var usersInRole = await _users.GetUsersInRoleAsync(role.Name);
            responses.Add(new AdminRoleSummaryResponse(role.Name, DescribeRole(role.Name), usersInRole.Count));
        }
        return Ok(responses);
    }

    [HttpPost("roles")]
    public async Task<ActionResult<AdminRoleDetailResponse>> CreateRole([FromBody] CreateRoleRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await AuditFailedAsync(AuditEvents.RoleCreated, "role_name_required", RoleTarget(request.Name), null);
            return BadRequest(new { message = "El nombre del rol es requerido." });
        }

        if (CanonicalRoles.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            await AuditFailedAsync(AuditEvents.RoleCreated, "canonical_role", RoleTarget(name), null);
            return Conflict(new { message = "No se puede crear un rol con un nombre canónico reservado." });
        }

        if (await _roles.RoleExistsAsync(name))
        {
            await AuditFailedAsync(AuditEvents.RoleCreated, "role_already_exists", RoleTarget(name), null);
            return Conflict(new { message = "El rol ya existe." });
        }

        var result = await _roles.CreateAsync(new IdentityRole(name));
        if (!result.Succeeded)
        {
            await AuditFailedAsync(AuditEvents.RoleCreated, IdentityReason(result), RoleTarget(name), null);
            return BadRequest(result.Errors);
        }

        await AuditSucceededAsync(AuditEvents.RoleCreated, RoleTarget(name), null);

        return Created($"/api/admin/roles/{name}",
            new AdminRoleDetailResponse(name, request.Description ?? "", false, 0, []));
    }

    [HttpDelete("roles/{roleName}")]
    public async Task<IActionResult> DeleteRole(string roleName)
    {
        if (CanonicalRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
        {
            await AuditFailedAsync(AuditEvents.RoleDeleted, "canonical_role", RoleTarget(roleName), null);
            return BadRequest(new { message = "Los roles canónicos no pueden ser eliminados." });
        }

        var role = await _roles.FindByNameAsync(roleName);
        if (role is null)
        {
            await AuditFailedAsync(AuditEvents.RoleDeleted, "role_not_found", RoleTarget(roleName), null);
            return NotFound(new { message = "Rol no encontrado." });
        }

        var usersInRole = await _users.GetUsersInRoleAsync(roleName);
        if (usersInRole.Count > 0)
        {
            await AuditFailedAsync(AuditEvents.RoleDeleted, "role_in_use", RoleTarget(roleName),
                new { assignedUsers = usersInRole.Count });
            return Conflict(new { message = $"No se puede eliminar el rol '{roleName}' porque tiene {usersInRole.Count} usuario(s) asignado(s)." });
        }

        var change = new
        {
            permissionsBefore = (await _roles.GetClaimsAsync(role))
                .Where(c => c.Type == "perm").Select(c => c.Value).OrderBy(x => x).ToArray()
        };

        var result = await _roles.DeleteAsync(role);
        if (!result.Succeeded)
        {
            await AuditFailedAsync(AuditEvents.RoleDeleted, IdentityReason(result), RoleTarget(roleName), change);
            return BadRequest(result.Errors);
        }

        await AuditSucceededAsync(AuditEvents.RoleDeleted, RoleTarget(roleName), change);
        return NoContent();
    }

    [HttpGet("roles/{roleName}/permissions")]
    public async Task<ActionResult<AdminRoleDetailResponse>> GetRolePermissions(string roleName)
    {
        var role = await _roles.FindByNameAsync(roleName);
        if (role is null)
            return NotFound(new { message = "Rol no encontrado." });

        var claims = await _roles.GetClaimsAsync(role);
        var perms = claims.Where(c => c.Type == "perm").Select(c => c.Value).OrderBy(x => x).ToArray();
        var usersInRole = await _users.GetUsersInRoleAsync(roleName);

        return Ok(new AdminRoleDetailResponse(
            roleName,
            DescribeRole(roleName),
            CanonicalRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase),
            usersInRole.Count,
            perms));
    }

    [HttpPut("roles/{roleName}/permissions")]
    public async Task<ActionResult<AdminRoleDetailResponse>> SetRolePermissions(
        string roleName, [FromBody] SetRolePermissionsRequest request)
    {
        var requestedPermissions = (request.Permissions ?? []).OrderBy(x => x).ToArray();

        var role = await _roles.FindByNameAsync(roleName);
        if (role is null)
        {
            await AuditFailedAsync(AuditEvents.RolePermissionsChanged, "role_not_found", RoleTarget(roleName),
                new { before = (string[]?)null, after = requestedPermissions });
            return NotFound(new { message = "Rol no encontrado." });
        }

        var currentClaims = (await _roles.GetClaimsAsync(role)).Where(c => c.Type == "perm").ToList();
        var change = new
        {
            before = currentClaims.Select(c => c.Value).OrderBy(x => x).ToArray(),
            after = requestedPermissions
        };

        var invalidPerms = requestedPermissions
            .Where(p => !PermissionCatalog.All.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (invalidPerms.Count > 0)
        {
            await AuditFailedAsync(AuditEvents.RolePermissionsChanged, "unknown_permission", RoleTarget(roleName), change);
            return BadRequest(new { message = $"Permisos no válidos: {string.Join(", ", invalidPerms)}" });
        }

        foreach (var claim in currentClaims)
        {
            var removed = await _roles.RemoveClaimAsync(role, claim);
            if (!removed.Succeeded)
            {
                await AuditFailedAsync(AuditEvents.RolePermissionsChanged, IdentityReason(removed), RoleTarget(roleName), change);
                return BadRequest(removed.Errors);
            }
        }

        foreach (var perm in requestedPermissions)
        {
            var added = await _roles.AddClaimAsync(role, new Claim("perm", perm));
            if (!added.Succeeded)
            {
                await AuditFailedAsync(AuditEvents.RolePermissionsChanged,
                    "previous_permissions_removed: " + IdentityReason(added), RoleTarget(roleName), change);
                return BadRequest(added.Errors);
            }
        }

        await AuditSucceededAsync(AuditEvents.RolePermissionsChanged, RoleTarget(roleName), change);

        var usersInRole = await _users.GetUsersInRoleAsync(roleName);
        return Ok(new AdminRoleDetailResponse(
            roleName,
            DescribeRole(roleName),
            CanonicalRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase),
            usersInRole.Count,
            requestedPermissions));
    }

    // ──────────────────────────────────────────────────────────────────
    // AUDIT (G1-T6)
    // ──────────────────────────────────────────────────────────────────
    //
    // Ordering: each event is written right after the Identity result it describes and
    // before the response is built, so nothing that runs afterwards (building the response
    // DTO, serialisation) can skip it. Identity lives in SQL Server and the audit trail in
    // PostgreSQL, so the two writes cannot share a transaction: the mutation commits first
    // and the audit write follows. A failing audit write is logged at Error level and
    // swallowed — it must not turn a mutation that already happened into an error response
    // the administrator would then retry. CancellationToken.None keeps a client disconnect
    // from cancelling the audit write of a mutation that is already committed.
    //
    // Payloads never carry passwords, reset tokens or any other credential: only the actor,
    // the target (user id/email or role name), a before/after summary and the outcome.

    private Task AuditSucceededAsync(string eventType, object target, object? change) =>
        WriteAuditAsync(eventType, "succeeded", reason: null, target, change);

    private Task AuditFailedAsync(string eventType, string reason, object target, object? change) =>
        WriteAuditAsync(eventType, "failed", reason, target, change);

    private async Task WriteAuditAsync(string eventType, string outcome, string? reason, object target, object? change)
    {
        var actorUserId = ActorUserId();
        var payload = new
        {
            actorUserId,
            actorEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"),
            action = eventType,
            target,
            change,
            outcome,
            reason
        };

        try
        {
            await _audit.WriteAsync(
                eventType,
                payload,
                correlationId: HttpContext?.TraceIdentifier,
                traceId: Activity.Current?.TraceId.ToString(),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Audit write failed for {EventType} (outcome {Outcome}) by actor {ActorUserId}; the administrative operation itself is not rolled back.",
                eventType, outcome, actorUserId);
        }
    }

    private string? ActorUserId() =>
        User.FindFirstValue("uid") ??
        User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        User.FindFirstValue("sub");

    private static object UserTarget(string? userId, string? email) => new { userId, email };

    private static object UserTarget(AppUser user) => UserTarget(user.Id, user.Email);

    private static object RoleTarget(string? roleName) => new { roleName };

    private static object LockoutChange(DateTimeOffset? before, DateTimeOffset? after) =>
        new { lockoutEndBefore = before, lockoutEndAfter = after };

    /// <summary>Identity error codes only: descriptions can echo user input.</summary>
    private static string IdentityReason(IdentityResult result) =>
        "identity_error: " + string.Join(",", result.Errors.Select(e => e.Code));

    // ──────────────────────────────────────────────────────────────────
    // HELPERS
    // ──────────────────────────────────────────────────────────────────

    private async Task<AdminUserResponse> BuildAdminUserResponseAsync(AppUser user)
    {
        var authenticatedUser = await _tokens.BuildAuthenticatedUserAsync(user);
        var status = user.LockoutEnd is { } lockoutEnd && lockoutEnd > DateTimeOffset.UtcNow
            ? "Blocked"
            : user.EmailConfirmed
                ? "Active"
                : "Inactive";

        return new AdminUserResponse(
            authenticatedUser.Id,
            authenticatedUser.Email,
            authenticatedUser.Name,
            authenticatedUser.PrimaryRole,
            authenticatedUser.Roles,
            authenticatedUser.Permissions,
            status,
            null);
    }

    private async Task<(List<string> Roles, string? Error)> ValidateRolesAsync(IEnumerable<string>? requestedRoles)
    {
        var roles = (requestedRoles ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var roleName in roles)
        {
            if (!await _roles.RoleExistsAsync(roleName))
                return ([], $"El rol '{roleName}' no existe.");
        }

        return (roles, null);
    }

    private static string DescribeRole(string roleName) => roleName switch
    {
        "Admin"    => "Catálogos, routing, vault admin, usuarios/roles y lectura completa.",
        "Operator" => "Operación issuer, cards, billing operativo, simulator, antifraud y disputas.",
        "Auditor"  => "Lectura de auditoría, ledger, statements, settlement, disputas y monitor.",
        _          => "Rol personalizado."
    };
}
