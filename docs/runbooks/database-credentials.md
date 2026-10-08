# Database credentials

No connection string password, and no default `postgres/postgres` credential, is committed anywhere in the backend (G1-T7). Every service reads its connection strings from the environment and refuses to start without them.

## Required variables

| Service | Configuration key | Environment variable | Database |
|---|---|---|---|
| CardVault.Api | `ConnectionStrings:Postgres` | `ConnectionStrings__Postgres` | `cardvault` (PostgreSQL) |
| CardVault.Api | `ConnectionStrings:SqlServerIdentity` | `ConnectionStrings__SqlServerIdentity` | Identity (SQL Server) |
| IsoSwitch.Api | `ConnectionStrings:Postgres` | `ConnectionStrings__Postgres` | `isoswitch` (PostgreSQL) |
| IsoAudit.Api | `ConnectionStrings:IsoSwitchDb` | `ConnectionStrings__IsoSwitchDb` | `isoswitch` (PostgreSQL, read side) |

Keep the password out of the connection string itself. Npgsql reads the standard `PGPASSWORD` environment variable (and a `PGPASSFILE`) when the connection string has no `Password`, so the recommended shape is:

```text
ConnectionStrings__Postgres=Host=<host>;Port=5432;Database=isoswitch;Username=<user>
PGPASSWORD  (set from your secrets manager)
```

A connection string with an inline password still works at runtime, but it must only ever come from the environment or a secrets manager, never from a committed file. `CommittedConfigSecretShapeTests` and the gitleaks rule `inline-connection-string-password` fail on any committed inline password (`Password` or `Pwd` keyword).

## Startup failures

Each service checks its connection strings before the host is built, in every environment (Development included). A missing or blank value stops startup with one of these messages:

| Service | Message starts with |
|---|---|
| CardVault.Api | `Missing required configuration value(s): ConnectionStrings:Postgres, ConnectionStrings:SqlServerIdentity.` (only the missing keys are listed) |
| IsoSwitch.Api | `Missing required configuration value: ConnectionStrings:Postgres.` |
| IsoAudit.Api | `Missing required configuration value: ConnectionStrings:IsoSwitchDb.` |

Fix: set the variable named in the message (table above) and restart. IsoSwitch and IsoAudit used to start without it (IsoSwitch from the committed base `appsettings.json`, IsoAudit from a compiled-in fallback); they no longer do.

## Local development

The committed `appsettings.Development.json` files carry empty connection strings on purpose. Before `dotnet run`, set the variables in the shell (PowerShell shown):

```powershell
$env:PGPASSWORD = "<your local postgres password>"
$env:ConnectionStrings__Postgres = "Host=localhost;Port=15432;Database=isoswitch;Username=postgres"      # IsoSwitch.Api
$env:ConnectionStrings__IsoSwitchDb = "Host=localhost;Port=15432;Database=isoswitch;Username=postgres"  # IsoAudit.Api
```

Port `15432` is the host port the compose stack publishes for PostgreSQL; use `5432` for a native install. CardVault additionally needs `ConnectionStrings__SqlServerIdentity`.

## Docker Compose

`backend/deploy/docker-compose.yml` takes the PostgreSQL password from a single variable, `PGPASSWORD`, in `backend/deploy/.env`:

- the `postgres` container is initialised with it (`POSTGRES_PASSWORD: ${PGPASSWORD:?…}`); compose refuses to start the stack when it is unset;
- `cardvault`, `isoswitch` and `isoaudit` receive it through `env_file: .env`, and their connection strings (set in the compose file, without a password) authenticate through it.

An existing `postgres_data` volume keeps the password it was initialised with. When moving an existing stack off the old default, either set `PGPASSWORD` to the current password or change it in the server (`ALTER USER postgres PASSWORD '…'`) before restarting the services.

Remove any inline password from `ConnectionStrings__*` entries in your local `.env`: the compose file now sets each service's connection string explicitly, so those entries are no longer used by the stack.

## Tests

- Unit and HTTP tests (`CardVault.Tests`, `IsoSwitch.Tests`, `IsoAudit.Tests`) inject password-less placeholder connection strings in their `WebApplicationFactory` and replace the DbContexts with InMemory providers; no database is contacted.
- `IsoSwitch.IntegrationTests` and `CardVault.IntegrationTests` read `ISOSWITCH_TEST_POSTGRES` / `CARDVAULT_TEST_POSTGRES` or start a Testcontainers PostgreSQL; they do not read `appsettings*.json`.
