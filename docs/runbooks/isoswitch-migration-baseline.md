# IsoSwitch migration baseline (Gate 0 / T8)

**Audience:** whoever deploys IsoSwitch or keeps a long-lived development database.
**Applies to:** `IsoSwitch.Infrastructure.Persistence` (PostgreSQL / Npgsql), the `IsoSwitchDbContext`
schema. `IsoAudit.Api` shares this context read/write but never migrates it: IsoSwitch owns the schema.

## What changed

The IsoSwitch EF Core migration chain was collapsed into a single migration,
`20261006185159_InitialBaseline`, generated from the current model.

Every earlier migration file was deleted. Their IDs (`20260119000200_InitialIsoSwitch` through
`20260417150930_FinalMigration`) no longer exist in the assembly. A database whose
`__EFMigrationsHistory` table references one of them cannot be migrated by this build; see
"Database with pre-baseline history rows" below.

## Why

- 12 of the 15 files under `Migrations/` had no `[Migration]` attribute and no `.Designer.cs`, so
  EF Core never discovered them. The chain that looked like twelve incremental steps was, to EF Core,
  a single migration: `20260417150930_FinalMigration`.
- `FinalMigration` created all 11 tables from scratch. On an empty database that worked, which is why
  a clean `Migrate()` passed on a real PostgreSQL server in the first integration run. On a database
  that already had the schema (Development provisions it with `EnsureCreated()`), the same
  `Migrate()` failed with `42P07: relation "AuditEvents" already exists`.
- `IsoSwitch.Api/Program.cs` silenced `PendingModelChangesWarning`, hiding any drift between model
  and snapshot from the logs.

No production database has ever been created from this chain, so there was no history worth
preserving. Rebuilding the chain from the current model was the smallest correct fix.
`IsoSwitch.IntegrationTests` now proves on a real PostgreSQL server that:

- the baseline applies to an empty database, writes exactly one history row, leaves no pending
  migration and no pending model change (`Migrations/CleanDatabaseMigrateTest.cs`);
- an `EnsureCreated()` database adopts the baseline and then migrates as a no-op, idempotently
  (`Migrations/BaselineAdoptionTest.cs`);
- a `Transaction` and its `iso_message_logs` rows round-trip through the migrated schema, including the
  unique index on `(TraceId, Direction)` (`Persistence/TransactionPersistenceSmokeTest.cs`).

## Which path your database is on

| Database state | What happens at startup (non-Development) | What you must do |
|---|---|---|
| Empty (no tables) | `Migrate()` applies the baseline. | Nothing. |
| Schema present, **no** `__EFMigrationsHistory` table or no rows (created with `EnsureCreated()`) | `MigrationBaselineAdoption` inserts the baseline row and logs a warning; `Migrate()` is then a no-op. | Nothing, but read the log line and keep it. |
| `__EFMigrationsHistory` has the baseline row | `Migrate()` is a no-op (or applies later migrations). | Nothing. |
| `__EFMigrationsHistory` has **pre-baseline** rows (for example `20260417150930_FinalMigration`) | `Migrate()` fails: it would try to create tables that already exist (`42P07`). | Manual step below. |
| Development environment | `EnsureCreated()` as before; migrations are not involved. | Nothing. |

### Baseline adoption at startup

`MigrationBaselineAdoption.TryAdoptBaselineAsync` (in `IsoSwitch.Infrastructure.Persistence/Migrations`)
binds the shared helper `BuildingBlocks.Persistence.MigrationBaselineAdopter` with the IsoSwitch
baseline ID and the `Transactions` probe table. It runs before `Migrate()` on the non-Development path
of `IsoSwitch.Api/Program.cs` and:

1. Returns `false` unless the provider is Npgsql and the database is reachable.
2. Returns `false` unless the assembly contains exactly one migration, the baseline. If more
   migrations exist, an `EnsureCreated()` database matches the *current* model, not the baseline
   schema, and marking only the baseline as applied would make later migrations fail. In that case
   the helper logs an error and leaves the decision to a human (see manual SQL below).
3. Returns `false` when any migration is already recorded as applied.
4. Returns `false` when the `Transactions` table does not exist (empty database: `Migrate()` owns it).
5. Otherwise creates `__EFMigrationsHistory` if missing, inserts
   `('20261006185159_InitialBaseline', '<EF Core product version>')`, logs a warning, and returns `true`.

The helper is idempotent: on the second run step 3 short-circuits.

### Manual SQL equivalent

If you prefer to adopt by hand, or the automatic path refused (step 2), run this against the
IsoSwitch database **after confirming the schema matches the current model**:

```sql
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    character varying(150) NOT NULL,
    "ProductVersion" character varying(32)  NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261006185159_InitialBaseline', '9.0.0')
ON CONFLICT ("MigrationId") DO NOTHING;
```

`ProductVersion` is informational; use the EF Core version the service is built with.

### Database with pre-baseline history rows

Such a database was created by `Migrate()` from an older build, so it holds a
`20260417150930_FinalMigration` row. `FinalMigration` did create the full current schema, so the
tables are expected to match the model, but verify before trusting that. Recommended handling:

1. Confirm there is no data worth keeping (there should not be; no deployment has succeeded).
2. Drop and recreate the database, then let `Migrate()` apply the baseline.

If data must be kept, compare the live schema against `dotnet ef migrations script` for the baseline
on an empty scratch database, reconcile the differences by hand, then delete the old row and insert
the baseline row as in the manual SQL above. This is a one-off, human-driven operation; do not
script it into startup.

## Running the integration tests locally

```powershell
# Optional: point at a running server (admin connection; the fixture creates is_it_<guid> databases).
# Without it, a postgres:16-alpine Testcontainer is started (Docker required).
$env:ISOSWITCH_TEST_POSTGRES = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres"
dotnet test backend/services/IsoSwitch/tests/IsoSwitch.IntegrationTests/IsoSwitch.IntegrationTests.csproj -m:1 -p:UseSharedCompilation=false -nodereuse:false
```

The role behind that connection string needs `CREATEDB` and the right to
`DROP DATABASE ... WITH (FORCE)` (PostgreSQL 13 or newer); see
`CardVault.IntegrationTests/README.md` for the same contract on the CardVault side. The server
lifecycle is shared through `backend/shared/tests/Postgres.TestSupport`.

## Adding migrations from here

Normal workflow; the baseline is an ordinary first migration:

```powershell
dotnet ef migrations add <Name> `
  --project backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.Persistence/IsoSwitch.Infrastructure.Persistence.csproj `
  --startup-project backend/services/IsoSwitch/src/IsoSwitch.Api/IsoSwitch.Api.csproj `
  --context IsoSwitchDbContext -- --environment Development
```

Keep the generated `.Designer.cs` and `IsoSwitchDbContextModelSnapshot.cs` intact. `CleanDatabaseMigrateTest`
asserts that no migration is pending after `Migrate()` and that `HasPendingModelChanges()` is false,
so a trimmed Designer or a stale snapshot fails CI on the integration job.

Note for the day a second migration lands: step 2 of the adoption helper will start refusing
`EnsureCreated()` databases. Development databases should then be recreated (they are disposable),
or adopted by hand by inserting every migration ID that the current model already includes.

## Observations recorded while generating the baseline (not changed here)

`Up()` creates 11 tables, 13 indexes and no foreign keys. Worth a later task:

- The schema holds no `numeric` column at all: amounts travel as ISO strings (`Transactions.Amount12`
  is `text`). That is correct for an ISO 8583 switch, but any future reporting over amounts must parse.
- 38 columns are unbounded `text`; in `Transactions` that includes `TraceId`, `Stan`, `ConnectorId`,
  `Status`, `TxType` and the cached ISO fields, with no length contract. `iso_message_logs` and
  `AuditEvents` do declare lengths.
- The deleted `20260119000600_IsoMessageLogs` migration created a table named `IsoMessageLogs`, while
  the model maps the entity to `iso_message_logs`. Any database that ran that old file (none should
  exist) carries an orphan `IsoMessageLogs` table the model never reads.
- `Transactions.RequestJson` is persisted in clear (Gate 0 / T10 masks it).
