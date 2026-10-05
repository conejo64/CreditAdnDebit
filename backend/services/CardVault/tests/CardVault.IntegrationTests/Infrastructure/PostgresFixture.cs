using CardVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CardVault.IntegrationTests.Infrastructure;

/// <summary>
/// Collection-level fixture that provides a real PostgreSQL server for the integration tests.
///
/// Server resolution order:
/// <list type="number">
///   <item>
///     <c>CARDVAULT_TEST_POSTGRES</c> environment variable, when set: an Npgsql connection string
///     pointing at an already running server (the CI service container, or a local instance).
///     The database named in that string is used only as the administrative connection.
///   </item>
///   <item>
///     Otherwise a <c>postgres:16-alpine</c> Testcontainer is started for the lifetime of the collection.
///   </item>
/// </list>
///
/// In both modes every call to <see cref="CreateDatabaseAsync"/> creates a brand-new, uniquely named
/// database on that server, so each test runs on an empty schema and unique indexes
/// (<c>Customers.DocumentId</c>, <c>Customers.CustomerNumber</c>, ...) can never collide across tests.
/// Databases created during the run are dropped when the fixture is disposed.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "CARDVAULT_TEST_POSTGRES";
    private const string Image = "postgres:16-alpine";

    private readonly List<string> _createdDatabases = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PostgreSqlContainer? _container;
    private string? _adminConnectionString;

    /// <summary>Human-readable description of where the server came from, for diagnostics.</summary>
    public string Source { get; private set; } = "unresolved";

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(external))
        {
            _adminConnectionString = external;
            Source = $"env:{EnvironmentVariable}";
            return;
        }

        _container = new PostgreSqlBuilder()
            .WithImage(Image)
            .Build();

        await _container.StartAsync();
        _adminConnectionString = _container.GetConnectionString();
        Source = $"testcontainer:{Image}";
    }

    public async Task DisposeAsync()
    {
        if (_adminConnectionString is not null)
        {
            foreach (var database in _createdDatabases)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(_adminConnectionString);
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)";
                    await command.ExecuteNonQueryAsync();
                }
                catch
                {
                    // Best-effort cleanup; a leaked test database never fails the run.
                }
            }
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        _gate.Dispose();
    }

    /// <summary>
    /// Creates a new empty database on the resolved server and returns a connection string to it.
    /// No schema is created: callers decide between <c>EnsureCreated()</c> and <c>Migrate()</c>.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken ct = default)
    {
        if (_adminConnectionString is null)
        {
            throw new InvalidOperationException("PostgresFixture has not been initialized.");
        }

        var name = $"cv_it_{Guid.NewGuid():N}";

        await _gate.WaitAsync(ct);
        try
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync(ct);
            _createdDatabases.Add(name);
        }
        finally
        {
            _gate.Release();
        }

        var builder = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = name };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Builds <see cref="DbContextOptions{CardVaultDbContext}"/> for a fresh database.
    /// </summary>
    public async Task<DbContextOptions<CardVaultDbContext>> CreateOptionsAsync(CancellationToken ct = default)
    {
        var connectionString = await CreateDatabaseAsync(ct);
        return new DbContextOptionsBuilder<CardVaultDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }

    /// <summary>
    /// Creates a <see cref="CardVaultDbContext"/> on a fresh database whose schema was created with
    /// <c>EnsureCreated()</c> from the current model. This mirrors how the Development environment
    /// provisions its schema today and keeps the behaviour tests independent from the migration chain
    /// (see <c>CleanDatabaseMigrateTest</c> for the migration path).
    /// </summary>
    public async Task<CardVaultDbContext> CreateDbContextAsync(CancellationToken ct = default)
    {
        var options = await CreateOptionsAsync(ct);
        var context = new CardVaultDbContext(options);
        await context.Database.EnsureCreatedAsync(ct);
        return context;
    }

    /// <summary>
    /// Creates a second context on the same database, with a clean change tracker.
    /// Useful for read-after-write assertions.
    /// </summary>
    public static CardVaultDbContext CreateSiblingContext(CardVaultDbContext existing)
    {
        var connectionString = existing.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The existing context has no connection string.");

        var options = new DbContextOptionsBuilder<CardVaultDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CardVaultDbContext(options);
    }
}
