using IsoAudit.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace IsoAudit.Tests.Security;

/// <summary>
/// G1-T7: IsoAudit.Api used to fall back to a compiled-in <c>postgres/postgres</c>
/// connection string when <c>ConnectionStrings:IsoSwitchDb</c> was not configured. The
/// fallback is gone: startup must refuse to run without the connection string and name the
/// missing key.
/// </summary>
public class DatabaseConnectionStringStartupTests
{
    private static Exception? StartWith(string environment, string connectionString)
    {
        using var factory = new IsoAuditWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(environment);
                b.UseSetting("ConnectionStrings:IsoSwitchDb", connectionString);
            });

        return Record.Exception(() => factory.CreateClient());
    }

    [Theory]
    [InlineData("Production", "")]
    [InlineData("Production", "   ")]
    [InlineData("Staging", "")]
    [InlineData("Development", "")]
    public void MissingIsoSwitchDbConnectionString_RefusesToStart_NamingTheKey(string environment, string value)
    {
        var exception = StartWith(environment, value);

        Assert.NotNull(exception);
        Assert.Contains("ConnectionStrings:IsoSwitchDb", exception!.ToString());
    }

    [Fact]
    public void ConfiguredIsoSwitchDbConnectionString_StartsInProduction()
    {
        var exception = StartWith("Production", IsoAuditWebApplicationFactory.TestIsoSwitchDbConnectionString);

        Assert.Null(exception);
    }
}
