# CardVault migration baseline (Gate 0 / T7)

**Audience:** whoever deploys CardVault or keeps a long-lived development database.
**Applies to:** `CardVault.Infrastructure.Persistence` (PostgreSQL / Npgsql). The Identity context (SQL Server) is unaffected.

## What changed

The CardVault EF Core migration chain was collapsed into a single migration,
`20261006152735_InitialBaseline`, generated from the current model.

Every earlier migration file was deleted. Their IDs (`20260119000100_InitialCardVault` through
`20261005163714_AddCardProductInstallmentApr`) no longer exist in the assembly. A database whose
`__EFMigrationsHistory` table references one of them cannot be migrated by this build; see
"Database with pre-baseline history rows" below.

## Why

A clean `Database.Migrate()` on an empty PostgreSQL database failed with
`42P01: relation "Countries" does not exist`:

- 19 early migrations had no `[Migration]` attribute and no `.Designer.cs`, so EF Core never
  discovered them.
- The first discovered migration (`20260311014310_AddCustomerDataFields`) started by altering
  `Countries`, which nothing in the discovered chain had created.
- One later Designer deliberately omitted `BuildTargetModel`, so the snapshot was not trustworthy.

No production database has ever been created from this chain (no deployment has succeeded), so
there was no history worth preserving. Rebuilding the chain from the current model was the smallest
correct fix. `CardVault.IntegrationTests/Migrations/CleanDatabaseMigrateTest.cs` now proves on a real
PostgreSQL server that the baseline applies to an empty database, writes exactly one history row,
and leaves no pending migration (model and snapshot agree).

## Which path your database is on

| Database state | What happens at startup (non-Development) | What you must do |
|---|---|---|
| Empty (no tables) | `Migrate()` applies the baseline. | Nothing. |
| Schema present, **no** `__EFMigrationsHistory` table or no rows (created with `EnsureCreated()`) | `MigrationBaselineAdoption` inserts the baseline row and logs a warning; `Migrate()` is then a no-op. | Nothing, but read the log line and keep it. |
| `__EFMigrationsHistory` has the baseline row | `Migrate()` is a no-op (or applies later migrations). | Nothing. |
| `__EFMigrationsHistory` has **pre-baseline** rows | `Migrate()` fails: it would try to create tables that already exist (`42P07`). | Manual step below. |
| Development environment | `EnsureCreated()` as before; migrations are not involved. | Nothing. |

### Baseline adoption at startup

`MigrationBaselineAdoption.TryAdoptBaselineAsync` (in `CardVault.Infrastructure.Persistence/Migrations`)
runs before `Migrate()` on the non-Development path of `CardVault.Api/Program.cs`. It:

1. Returns `false` unless the provider is Npgsql and the database is reachable.
2. Returns `false` unless the assembly contains exactly one migration, the baseline. If more
   migrations exist, an `EnsureCreated()` database matches the *current* model, not the baseline
   schema, and marking only the baseline as applied would make later migrations fail. In that case
   the helper logs an error and leaves the decision to a human (see manual SQL below).
3. Returns `false` when any migration is already recorded as applied.
4. Returns `false` when the `Customers` table does not exist (empty database: `Migrate()` owns it).
5. Otherwise creates `__EFMigrationsHistory` if missing, inserts
   `('20261006152735_InitialBaseline', '<EF Core product version>')`, logs a warning, and returns `true`.

The helper is idempotent: on the second run step 3 short-circuits. It is covered by
`CardVault.IntegrationTests/Migrations/BaselineAdoptionTest.cs`.

### Manual SQL equivalent

If you prefer to adopt by hand, or the automatic path refused (step 2), run this against the
CardVault database **after confirming the schema matches the current model**:

```sql
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    character varying(150) NOT NULL,
    "ProductVersion" character varying(32)  NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261006152735_InitialBaseline', '9.0.0')
ON CONFLICT ("MigrationId") DO NOTHING;
```

`ProductVersion` is informational; use the EF Core version the service is built with.

### Database with pre-baseline history rows

Such a database was created by `Migrate()` from an older build. Because the old chain never applied
cleanly, its schema cannot be assumed to match the model. Recommended handling:

1. Confirm there is no data worth keeping (there should not be; no deployment has succeeded).
2. Drop and recreate the database, then let `Migrate()` apply the baseline.

If data must be kept, compare the live schema against `dotnet ef migrations script` for the baseline
on an empty scratch database, reconcile the differences by hand, then delete the old rows and insert
the baseline row as in the manual SQL above. This is a one-off, human-driven operation; do not
script it into startup.

## Adding migrations from here

Normal workflow; the baseline is an ordinary first migration:

```powershell
dotnet ef migrations add <Name> `
  --project backend/services/CardVault/src/CardVault.Infrastructure.Persistence/CardVault.Infrastructure.Persistence.csproj `
  --startup-project backend/services/CardVault/src/CardVault.Api/CardVault.Api.csproj `
  --context CardVaultDbContext -- --environment Development
```

Keep the generated `.Designer.cs` and `CardVaultDbContextModelSnapshot.cs` intact. `CleanDatabaseMigrateTest`
asserts that no migration is pending after `Migrate()`, so a trimmed Designer or a stale snapshot
fails CI on the integration job.

Note for the day a second migration lands: step 2 of the adoption helper will start refusing
`EnsureCreated()` databases. Development databases should then be recreated (they are disposable),
or adopted by hand by inserting every migration ID that the current model already includes.

## Observations recorded while generating the baseline (not changed here)

`Up()` creates 53 tables, 80 indexes and 16 foreign keys. Two things worth a later task:

- 96 money/rate columns are `numeric` without precision or scale (only one `numeric(18,2)` exists).
  PostgreSQL stores them exactly, so balances are not at risk, but the model declares no contract
  on scale.
- `Statements` date columns are `timestamp with time zone`; the T4b normalization keeps them UTC.
  A `date` column type for calendar dates remains an option.
