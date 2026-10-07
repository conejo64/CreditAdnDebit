using Npgsql;
using Testcontainers.PostgreSql;

namespace Postgres.TestSupport;

/// <summary>
/// Lifecycle of a real PostgreSQL server for integration tests, shared by every service.
///
/// Server resolution order:
/// <list type="number">
///   <item>
///     The environment variable named by <see cref="EnvironmentVariable"/>, when set: an Npgsql
///     connection string pointing at an already running server (the CI service container, or a
///     local instance). The database named in that string is used only as the administrative
///     connection; it needs <c>CREATEDB</c> and the right to <c>DROP DATABASE ... WITH (FORCE)</c>
///     (PostgreSQL 13 or newer).
///   </item>
///   <item>
///     Otherwise a <see cref="Image"/> Testcontainer is started for the lifetime of this instance.
///   </item>
/// </list>
///
/// In both modes every call to <see cref="CreateDatabaseAsync"/> creates a brand-new, uniquely named
/// database (<c>{prefix}{guid}</c>) on that server, so each test runs on an empty schema and unique
/// indexes can never collide across tests. Databases created during the run are dropped on
/// <see cref="DisposeAsync"/>; a failed drop is reported through <c>diagnostics</c> and never fails the run.
///
/// The class is xunit-agnostic on purpose: a service fixture wraps it, implements
/// <c>IAsyncLifetime</c>, and adds the <c>DbContext</c>-typed helpers.
/// </summary>
public sealed class PostgresTestServer : IAsyncDisposable
{
    public const string DefaultImage = "postgres:16-alpine";

    private readonly string _databasePrefix;
    private readonly Action<string> _diagnostics;
    private readonly List<string> _createdDatabases = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PostgreSqlContainer? _container;
    private string? _adminConnectionString;

    /// <param name="environmentVariable">
    /// Name of the environment variable that may hold an administrative connection string
    /// (for example <c>CARDVAULT_TEST_POSTGRES</c>).
    /// </param>
    /// <param name="databasePrefix">
    /// Prefix for the databases this server creates (for example <c>cv_it_</c>). Keep it unique per
    /// service so two test projects sharing one server never collide and cleanup stays scoped.
    /// </param>
    /// <param name="diagnostics">
    /// Sink for cleanup failures. Defaults to <c>stderr</c>; fixtures usually forward to xunit's
    /// <c>IMessageSink</c> as well.
    /// </param>
    /// <param name="image">Docker image used when no external server is configured.</param>
    public PostgresTestServer(
        string environmentVariable,
        string databasePrefix,
        Action<string>? diagnostics = null,
        string image = DefaultImage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariable);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        EnvironmentVariable = environmentVariable;
        Image = image;
        _databasePrefix = databasePrefix;
        _diagnostics = diagnostics ?? Console.Error.WriteLine;
    }

    /// <summary>Environment variable consulted before falling back to a Testcontainer.</summary>
    public string EnvironmentVariable { get; }

    /// <summary>Docker image used for the Testcontainer fallback.</summary>
    public string Image { get; }

    /// <summary>Human-readable description of where the server came from, for diagnostics.</summary>
    public string Source { get; private set; } = "unresolved";

    /// <summary>Resolves the server: external connection string, or a started container.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
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

        await _container.StartAsync(ct);
        _adminConnectionString = _container.GetConnectionString();
        Source = $"testcontainer:{Image}";
    }

    /// <summary>
    /// Creates a new empty database on the resolved server and returns a connection string to it.
    /// No schema is created: callers decide between <c>EnsureCreated()</c> and <c>Migrate()</c>.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken ct = default)
    {
        if (_adminConnectionString is null)
        {
            throw new InvalidOperationException($"{nameof(PostgresTestServer)} has not been initialized.");
        }

        var name = $"{_databasePrefix}{Guid.NewGuid():N}";

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

    /// <summary>Drops every database created by this instance, then stops the container when one was started.</summary>
    public async ValueTask DisposeAsync()
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
                catch (Exception ex)
                {
                    // Best-effort cleanup: a leaked test database never fails the run, but it must
                    // never be silent either, or a misconfigured admin connection goes unnoticed.
                    _diagnostics(
                        $"{nameof(PostgresTestServer)}: could not drop test database \"{database}\" on {Source}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        _gate.Dispose();
    }
}
