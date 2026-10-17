using IsoSwitch.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace IsoSwitch.Tests.Security;

/// <summary>
/// G1-T7: the committed appsettings no longer carry a default <c>postgres/postgres</c>
/// connection string, so IsoSwitch.Api must refuse to start when
/// <c>ConnectionStrings:Postgres</c> is absent from every configuration source, naming the
/// missing key, instead of starting and failing on the first database call.
/// </summary>
public class DatabaseConnectionStringStartupTests
{
    private static Exception? StartWith(string environment, string connectionString)
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(environment);
                b.UseSetting("ConnectionStrings:Postgres", connectionString);
            });

        return Record.Exception(() => factory.CreateClient());
    }

    [Theory]
    [InlineData("Production", "")]
    [InlineData("Production", "   ")]
    [InlineData("Staging", "")]
    [InlineData("Development", "")]
    public void MissingPostgresConnectionString_RefusesToStart_NamingTheKey(string environment, string value)
    {
        var exception = StartWith(environment, value);

        Assert.NotNull(exception);
        Assert.Contains("ConnectionStrings:Postgres", exception!.ToString());
    }

    [Fact]
    public void ConfiguredPostgresConnectionString_StartsInProduction()
    {
        var exception = StartWith("Production", IsoSwitchWebApplicationFactory.TestPostgresConnectionString);

        Assert.Null(exception);
    }
}
