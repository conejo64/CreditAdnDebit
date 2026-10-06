# CardVault migrations

This folder's `Migrations/` directory holds a single baseline, `20261006152735_InitialBaseline`,
generated from the current `CardVaultDbContext` model on 2026-10-06 (Gate 0 / T7). The previous chain
was deleted because it could not be applied to an empty database; the full story, the upgrade path for
existing databases and the manual SQL live in `docs/runbooks/cardvault-migration-baseline.md`.

Rules of the road:

- Generate migrations with `dotnet ef`, using `CardVault.Api` as the startup project (it references
  `Microsoft.EntityFrameworkCore.Design`). Pass `-- --environment Development` so the host builds
  without production secrets; `CardVaultDbContextFactory` supplies the design-time connection.
- Never hand-trim a `.Designer.cs` or the snapshot. `CleanDatabaseMigrateTest` (integration project)
  fails when the model and the snapshot disagree.
- `MigrationBaselineAdoption` lets a database created with `EnsureCreated()` adopt the baseline
  at startup. It only does so while the chain is exactly one migration; see the runbook before
  adding a second one.
- The Identity context (SQL Server) has its own chain under `CardVault.Infrastructure.Identity` and
  is not covered by any of this.
