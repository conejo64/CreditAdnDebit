using IsoSwitch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Postgres.TestSupport;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace IsoSwitch.IntegrationTests.Infrastructure;

/// <summary>
/// Collection-level fixture that provides a real PostgreSQL server for the IsoSwitch integration tests.
///
/// The server lifecycle (external <c>ISOSWITCH_TEST_POSTGRES</c> connection string or a
/// <c>postgres:16-alpine</c> Testcontainer, one fresh database per <see cref="CreateDatabaseAsync"/>,
/// cleanup with logging) lives in the shared <see cref="PostgresTestServer"/>; this class adapts it to
/// xunit and to <see cref="IsoSwitchDbContext"/>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "ISOSWITCH_TEST_POSTGRES";
    private const string DatabasePrefix = "is_it_";

    private readonly PostgresTestServer _server;

    /// <param name="diagnostics">xunit diagnostic sink; cleanup failures are reported here (and on stderr).</param>
    public PostgresFixture(IMessageSink diagnostics)
    {
        _server = new PostgresTestServer(
            EnvironmentVariable,
            DatabasePrefix,
            message =>
            {
                diagnostics.OnMessage(new DiagnosticMessage(message));
                Console.Error.WriteLine(message);
            });
    }

    /// <summary>Human-readable description of where the server came from, for diagnostics.</summary>
    public string Source => _server.Source;

    public Task InitializeAsync() => _server.InitializeAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    /// <summary>
    /// Creates a new empty database on the resolved server and returns a connection string to it.
    /// No schema is created: callers decide between <c>EnsureCreated()</c> and <c>Migrate()</c>.
    /// </summary>
    public Task<string> CreateDatabaseAsync(CancellationToken ct = default) => _server.CreateDatabaseAsync(ct);

    /// <summary>Builds <see cref="DbContextOptions{IsoSwitchDbContext}"/> for a fresh, empty database.</summary>
    public async Task<DbContextOptions<IsoSwitchDbContext>> CreateOptionsAsync(CancellationToken ct = default)
    {
        var connectionString = await CreateDatabaseAsync(ct);
        return BuildOptions(connectionString);
    }

    /// <summary>
    /// Creates a second context on the same database, with a clean change tracker.
    /// Useful for read-after-write assertions.
    /// </summary>
    public static IsoSwitchDbContext CreateSiblingContext(IsoSwitchDbContext existing)
    {
        var connectionString = existing.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The existing context has no connection string.");

        return new IsoSwitchDbContext(BuildOptions(connectionString));
    }

    private static DbContextOptions<IsoSwitchDbContext> BuildOptions(string connectionString)
        => new DbContextOptionsBuilder<IsoSwitchDbContext>()
            .UseNpgsql(connectionString)
            .Options;
}
