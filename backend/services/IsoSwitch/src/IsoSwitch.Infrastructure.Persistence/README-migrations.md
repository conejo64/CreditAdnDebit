# IsoSwitch migrations

This folder's `Migrations/` directory holds a single baseline, `20261006185159_InitialBaseline`,
generated from the current `IsoSwitchDbContext` model on 2026-10-06 (Gate 0 / T8). The previous chain
was deleted because twelve of its files were never discovered by EF Core and the one that was failed on
any database that already had the schema; the full story, the upgrade path for existing databases and
the manual SQL live in `docs/runbooks/isoswitch-migration-baseline.md`.

Rules of the road:

- Generate migrations with `dotnet ef`, using `IsoSwitch.Api` as the startup project (it references
  `Microsoft.EntityFrameworkCore.Design`). Pass `-- --environment Development` so the host builds
  without production secrets; `IsoSwitchDbContextFactory` supplies the design-time connection from
  `ISOSWITCH_POSTGRES` (no database needs to be reachable for `migrations add`).
- Never hand-trim a `.Designer.cs` or the snapshot. `CleanDatabaseMigrateTest` (in
  `IsoSwitch.IntegrationTests`) fails when the model and the snapshot disagree
  (`HasPendingModelChanges()`), and `IsoSwitch.Api/Program.cs` no longer silences
  `PendingModelChangesWarning`.
- `MigrationBaselineAdoption` (this folder) binds the shared `BuildingBlocks.Persistence.MigrationBaselineAdopter`
  so a database created with `EnsureCreated()` adopts the baseline at startup. It only does so while
  the chain is exactly one migration; see the runbook before adding a second one.
- IsoSwitch owns this schema. `IsoAudit.Api` reads and writes `IsoSwitchDbContext` but must never
  migrate it.
