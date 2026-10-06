# CardVault.IntegrationTests

Behaviour and migration tests for CardVault that run against a real PostgreSQL server
instead of the EF Core InMemory provider. Every test in this project carries
`[Trait("Category", "Integration")]`; `Infrastructure/CategoryTraitEnforcementTests.cs`
fails the run if a test class is added without it, so the solution-wide
`dotnet test --filter "Category!=Integration"` in CI keeps excluding this project.

## How the server is resolved

`Infrastructure/PostgresFixture.cs` resolves the server once per run, in this order:

1. `CARDVAULT_TEST_POSTGRES` — an Npgsql connection string to an already running server
   (the CI service container or a local instance).
2. Otherwise a `postgres:16-alpine` Testcontainer is started for the lifetime of the run
   (requires a running Docker engine).

Each test receives its own freshly created database (`cv_it_<guid>`), so unique indexes can never
collide across tests. All databases created during the run are dropped when the fixture disposes.

## `CARDVAULT_TEST_POSTGRES` is an administrative connection

The connection string is **not** used as the application connection. The fixture runs:

- `CREATE DATABASE "cv_it_<guid>"` for every test, and
- `DROP DATABASE IF EXISTS "cv_it_<guid>" WITH (FORCE)` during cleanup.

Requirements for the role and server behind that string:

- PostgreSQL **13 or newer** (`DROP DATABASE ... WITH (FORCE)` does not exist before 13).
- The role needs `CREATEDB`, and must be allowed to terminate other sessions on the databases it
  drops (`WITH (FORCE)` requires that the role is the database owner or a superuser).
- The database named in the string is only used to open the administrative session; nothing is
  created inside it. Pointing it at a shared database is safe, but do not point it at a server
  whose other databases matter to you: a misconfigured run could drop them only if their names
  match the `cv_it_` prefix, which is unlikely but worth knowing.

Example (local server):

```powershell
$env:CARDVAULT_TEST_POSTGRES = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres"
dotnet test backend/services/CardVault/tests/CardVault.IntegrationTests/CardVault.IntegrationTests.csproj -m:1 -p:UseSharedCompilation=false -nodereuse:false
```

Cleanup failures (for example a leaked database that could not be dropped) are written to the
xunit diagnostic sink and to `stderr`; they never fail the run.

## Schema provisioning inside the tests

- Behaviour tests (`IntegrationTestBase`) use `EnsureCreated()` from the current model, mirroring
  the Development environment.
- `Migrations/CleanDatabaseMigrateTest.cs` applies the migration chain with `Migrate()` on an
  empty database: this is the fresh-deployment path.
- `Migrations/BaselineAdoptionTest.cs` covers a database that already has the schema but no
  `__EFMigrationsHistory` rows (created with `EnsureCreated()`), the situation described in
  `docs/runbooks/cardvault-migration-baseline.md`.
