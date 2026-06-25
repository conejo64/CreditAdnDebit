using CardVault.Application.Contracts;
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

    public AdminController(
        UserManager<AppUser> users,
        RoleManager<IdentityRole> roles,
        TokenService tokens)
    {
        _users = users;
        _roles = roles;
        _tokens = tokens;
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
        var (validatedRoles, roleError) = await ValidateRolesAsync(request.Roles);
        if (roleError is not null)
            return BadRequest(new { message = roleError });

        if (validatedRoles.Count == 0)
            return BadRequest(new { message = "Al menos un rol es requerido." });

        var existing = await _users.FindByEmailAsync(request.Email);
        if (existing is not null)
            return Conflict(new { message = "El usuario ya existe." });

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true
        };

        var created = await _users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            return BadRequest(created.Errors);

        var roleResult = await _users.AddToRolesAsync(user, validatedRoles);
        if (!roleResult.Succeeded)
            return BadRequest(roleResult.Errors);

        var response = await BuildAdminUserResponseAsync(user);
        return Created($"/api/admin/users/{user.Id}", response);
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
            return NotFound(new { message = "Usuario no encontrado." });

        var result = await _users.DeleteAsync(user);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return NoContent();
    }

    [HttpPost("users/{id}/block")]
    public async Task<ActionResult<AdminUserResponse>> BlockUser(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
            return NotFound(new { message = "Usuario no encontrado." });

        // LockoutEnd en un año equivale a bloqueo indefinido operativo
        var result = await _users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(1));
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        await _users.SetLockoutEnabledAsync(user, true);
        return Ok(await BuildAdminUserResponseAsync(user));
    }

    [HttpPost("users/{id}/unblock")]
    public async Task<ActionResult<AdminUserResponse>> UnblockUser(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
            return NotFound(new { message = "Usuario no encontrado." });

        var result = await _users.SetLockoutEndDateAsync(user, null);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return Ok(await BuildAdminUserResponseAsync(user));
    }

    [HttpPost("users/{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetPasswordRequest request)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
            return NotFound(new { message = "Usuario no encontrado." });

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return NoContent();
    }

    [HttpPut("users/{id}/roles")]
    public async Task<ActionResult<AdminUserResponse>> UpdateUserRoles(string id, [FromBody] UpdateUserRolesRequest request)
    {
        var (validatedRoles, roleError) = await ValidateRolesAsync(request.Roles);
        if (roleError is not null)
            return BadRequest(new { message = roleError });

        if (validatedRoles.Count == 0)
            return BadRequest(new { message = "Al menos un rol es requerido." });

        var user = await _users.FindByIdAsync(id);
        if (user is null)
            return NotFound(new { message = "Usuario no encontrado." });

        var currentRoles = await _users.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            var removeResult = await _users.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
                return BadRequest(removeResult.Errors);
        }

        var addResult = await _users.AddToRolesAsync(user, validatedRoles);
        if (!addResult.Succeeded)
            return BadRequest(addResult.Errors);

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
        var user = await _users.FindByIdAsync(id);
        if (user is null)
            return NotFound(new { message = "Usuario no encontrado." });

        var invalidPerms = request.Permissions
            .Where(p => !PermissionCatalog.All.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (invalidPerms.Count > 0)
            return BadRequest(new { message = $"Permisos no válidos: {string.Join(", ", invalidPerms)}" });

        var currentClaims = (await _users.GetClaimsAsync(user)).Where(c => c.Type == "perm").ToList();
        if (currentClaims.Count > 0)
        {
            var removeResult = await _users.RemoveClaimsAsync(user, currentClaims);
            if (!removeResult.Succeeded)
                return BadRequest(removeResult.Errors);
        }

        if (request.Permissions.Length > 0)
        {
            var newClaims = request.Permissions.Select(p => new Claim("perm", p)).ToList();
            var addResult = await _users.AddClaimsAsync(user, newClaims);
            if (!addResult.Succeeded)
                return BadRequest(addResult.Errors);
        }

        return Ok(request.Permissions.OrderBy(x => x).ToArray());
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
            return BadRequest(new { message = "El nombre del rol es requerido." });

        if (CanonicalRoles.Contains(name, StringComparer.OrdinalIgnoreCase))
            return Conflict(new { message = "No se puede crear un rol con un nombre canónico reservado." });

        if (await _roles.RoleExistsAsync(name))
            return Conflict(new { message = "El rol ya existe." });

        var result = await _roles.CreateAsync(new IdentityRole(name));
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return Created($"/api/admin/roles/{name}",
            new AdminRoleDetailResponse(name, request.Description ?? "", false, 0, []));
    }

    [HttpDelete("roles/{roleName}")]
    public async Task<IActionResult> DeleteRole(string roleName)
    {
        if (CanonicalRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { message = "Los roles canónicos no pueden ser eliminados." });

        var role = await _roles.FindByNameAsync(roleName);
        if (role is null)
            return NotFound(new { message = "Rol no encontrado." });

        var usersInRole = await _users.GetUsersInRoleAsync(roleName);
        if (usersInRole.Count > 0)
            return Conflict(new { message = $"No se puede eliminar el rol '{roleName}' porque tiene {usersInRole.Count} usuario(s) asignado(s)." });

        var result = await _roles.DeleteAsync(role);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

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
        var role = await _roles.FindByNameAsync(roleName);
        if (role is null)
            return NotFound(new { message = "Rol no encontrado." });

        var invalidPerms = request.Permissions
            .Where(p => !PermissionCatalog.All.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (invalidPerms.Count > 0)
            return BadRequest(new { message = $"Permisos no válidos: {string.Join(", ", invalidPerms)}" });

        var currentClaims = (await _roles.GetClaimsAsync(role)).Where(c => c.Type == "perm").ToList();
        foreach (var claim in currentClaims)
            await _roles.RemoveClaimAsync(role, claim);

        foreach (var perm in request.Permissions)
            await _roles.AddClaimAsync(role, new Claim("perm", perm));

        var usersInRole = await _users.GetUsersInRoleAsync(roleName);
        return Ok(new AdminRoleDetailResponse(
            roleName,
            DescribeRole(roleName),
            CanonicalRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase),
            usersInRole.Count,
            request.Permissions.OrderBy(x => x).ToArray()));
    }

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
