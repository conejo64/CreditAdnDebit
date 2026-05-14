using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IsoSwitch.Infrastructure.Persistence;

public sealed class IsoSwitchDbContextFactory : IDesignTimeDbContextFactory<IsoSwitchDbContext>
{
    public IsoSwitchDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IsoSwitchDbContext>();

        var cs = Environment.GetEnvironmentVariable("ISOSWITCH_POSTGRES")
                 ?? "Host=localhost;Port=5432;Database=isoswitch;Username=postgres;Password=postgres";

        options.UseNpgsql(cs);
        return new IsoSwitchDbContext(options.Options);
    }
}
