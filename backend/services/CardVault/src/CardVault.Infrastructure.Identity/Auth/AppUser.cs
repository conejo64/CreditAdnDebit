using Microsoft.AspNetCore.Identity;

namespace CardVault.Infrastructure.Identity.Auth;

public sealed class AppUser : IdentityUser
{
    // Extend later (e.g., TenantId, DisplayName, etc.)
}