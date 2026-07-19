using CardVault.Infrastructure.Identity.Auth;
using CardVault.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace CardVault.Tests.Security;

/// <summary>
/// Verifies SEC-05 / identity-and-access "Non-Development never auto-seeds a
/// default admin": administrative user seeding is confined to Development, with
/// no compiled-in credential fallback (no <c>?? "admin@demo.com"</c> /
/// <c>?? "Admin1234!"</c>). Mirrors the pre-existing catalog-seed
/// <c>IsDevelopment()</c> gate. Role seeding stays unconditional.
/// </summary>
public class AdminSeedGateTests
{
    [Fact]
    public async Task Production_EmptyIdentityStore_DoesNotSeedAdminUser()
    {
        using var factory = new CardVaultWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                // Even with a valid admin seed config present, Production must
                // never auto-seed — proves the gate, not just an empty default.
                b.UseSetting("Seed:AdminEmail", "admin@demo.com");
                b.UseSetting("Seed:AdminPassword", "Admin1234!");
            });

        using var client = factory.CreateClient(); // must NOT throw
        Assert.NotNull(client);

        using var scope = factory.Services.CreateScope();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        Assert.Null(await userMgr.FindByEmailAsync("admin@demo.com"));
        Assert.Null(await userMgr.FindByEmailAsync("operator@demo.com"));
        Assert.Null(await userMgr.FindByEmailAsync("auditor@demo.com"));
    }

    [Fact]
    public async Task Development_EmptyIdentityStore_StillSeedsDefaultOperatorRolesAndAdminUser()
    {
        using var factory = new CardVaultWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Seed:AdminEmail", "admin@demo.com");
                b.UseSetting("Seed:AdminPassword", "Admin1234!");
            });

        using var client = factory.CreateClient();
        Assert.NotNull(client);

        using var scope = factory.Services.CreateScope();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        Assert.True(await roleMgr.RoleExistsAsync("Admin"));
        Assert.True(await roleMgr.RoleExistsAsync("Operator"));
        Assert.True(await roleMgr.RoleExistsAsync("Auditor"));

        var admin = await userMgr.FindByEmailAsync("admin@demo.com");
        Assert.NotNull(admin);
        Assert.Contains("Admin", await userMgr.GetRolesAsync(admin!));
    }
}
