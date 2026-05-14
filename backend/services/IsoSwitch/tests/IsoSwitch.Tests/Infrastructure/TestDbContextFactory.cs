using IsoSwitch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.Tests.Infrastructure;

public static class TestDbContextFactory
{
    public static IsoSwitchDbContext Create()
    {
        var options = new DbContextOptionsBuilder<IsoSwitchDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new IsoSwitchDbContext(options);
    }
}
