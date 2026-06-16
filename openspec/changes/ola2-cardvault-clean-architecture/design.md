# Design: Ola 2 Clean Architecture Layers (CardVault + IsoSwitch)
# Change: ola2-cardvault-clean-architecture

Behavior-preserving refactor. Code moves project-to-project; namespaces/`using`/DI/MediatR scans/`.csproj` follow. The 650-test suite (`dotnet test backend/CardSwitchPlatform.sln`) is the regression net, green per slice. No new tests, no TDD red/green — there is no new behavior to specify. Grounded against the live code; the critical correction to the prior draft is the reference-direction cycle (§9, ADR-6).

## 1. Overview

**Goal**: populate `Domain` + `Application` (today only `Class1.cs`) with real code, push messaging/notification adapters into `Infrastructure`, leave each `*.Api` a thin composition root, and enforce the inward dependency direction at the code level — without changing a single behavior. **Non-goals**: DDD aggregates / persistence↔domain mapping (Ola 4+), entity reorg, test rewriting to mocks, bug fixes, perf work, `IsoAudit` (untouched).

**Why this architecture**: a credible conventional .NET Clean Architecture layout for due-diligence. Hexagonal principle applied pragmatically: the dependency rule (`Api → Application → Domain`, `Api → Infrastructure`) is honored, but full dependency *inversion* (ports for everything) is deliberately deferred — we keep concrete dependencies where the moves require, documented honestly (ADR-1).

## 2. Layer Model

| Layer | Holds | References |
|-------|-------|-----------|
| **Domain** | enums, pure side-effect-free calculators (`CalculateMinimum`, `ApplyClosingTotals`, `ComputeAverageDailyBalance`, `MapResponseCode`); IsoSwitch `TransactionStateMachine`, txn constants, `OriginalDataElementsBuilder` | **nothing** inward-of-service (ARCH-DEP-1) |
| **Application** | CQRS handlers, business services, the ports the moves require | `Domain`, `BuildingBlocks`; **NOT** `Api`, **NOT** `Infrastructure.*` (ARCH-DEP-2) |
| **Infrastructure.*** | EF entities + `DbContext` (Persistence), Identity, Messaging (Kafka), Notifications (providers/templates) | `Domain` (+ `Application` for the ports it implements) |
| **Api** | `Program.cs`, Controllers/Endpoints, HTTP DTOs (`Contracts/`), Pci/Security/Vault host concerns, IsoSwitch dev TCP codec | `Application` + all `Infrastructure.*` (only layer that may) |

The **port pattern**: an abstraction declared in `Application`, implemented in `Infrastructure` (or `Api`), wired in `Program.cs`. We introduce ports ONLY where a concrete dependency would otherwise force `Application → Infrastructure` (forbidden). Infrastructure provides the impl and references `Application` to see the interface — direction stays inward.

## 3. CardVault Concrete Design

**New projects**: `CardVault.Infrastructure.Messaging`, `CardVault.Infrastructure.Notifications` (confirmed absent; must be created, added to `.sln`, referenced by `Api`).

**CV-S1 — enum relocation (ARCH-1/ARCH-2)**: enums are *embedded in entity files* (e.g. `LedgerEntryType` in `Billing/LedgerEntryEntity.cs`), not standalone. Extract each enum into its own file under `CardVault.Domain` with `namespace CardVault.Domain` (proper move; the prior draft's "keep old namespace" trick is rejected — it lies about ownership and a tidy `using CardVault.Domain;` rename is mechanical). `Infrastructure.Persistence` gains a `Domain` `<ProjectReference>` and a `using CardVault.Domain;` in each entity file. Extract pure calculators as static Domain types; remove `Class1.cs`.

**CV-S2+S3 — handlers + services + ports + MediatR (ARCH-3/4/5)**: ~31 handlers (`Features/**`) + ~30 business services (`Services/**`) move together (handlers reference services). The blocker: 30 services depend on `CardVaultDbContext` (61 uses), which lives in `Infrastructure.Persistence` — `Application` cannot reference it (ARCH-DEP-2). **Resolution (ADR-6)**: `Application` defines `ICardVaultDbContext` (the narrowest interface exposing the `DbSet<>`s + `SaveChangesAsync` the moved code uses); `CardVaultDbContext` implements it in Persistence; `Persistence` keeps its existing `→ Application` reference (now justified) and `Program.cs` registers `ICardVaultDbContext → CardVaultDbContext`. Handlers returning `IResult`/`Results.*` require `<FrameworkReference Include="Microsoft.AspNetCore.App" />` on `Application`. Global rename `CardVault.Api.{Services,Features}` → `CardVault.Application.*`; update MediatR scan in `Program.cs` to the Application marker.

**CV-S4 — Kafka (ARCH-6)**: move `SwitchTxnConsumer` + `AuthDecisionPublisher` to `Infrastructure.Messaging`. Keep the `IServiceProvider` service-locator unchanged (ARCH-INV-6) — type-based registrations stay in `Program.cs`.

**CV-S5 — Notifications (ARCH-7)**: move `Services/Notifications/**` providers, webhooks, `.cshtml` to `Infrastructure.Notifications`. Carry the `Content … CopyToOutputDirectory=PreserveNewest` (and any `RazorCompile Remove`) `ItemGroup` into the new `.csproj`; update the **test project's** template `Content Include` link to the new path.

**CV-S6 — thin Api (ARCH-8)**: nothing left to move; confirm only `Program.cs`/Controllers/`Contracts/`/host concerns remain; `public partial class Program {}` (line 575) preserved.

## 4. IsoSwitch Concrete Design

**IS-S1 (ARCH-9/10)**: `TransactionStateMachine`, txn constants, `OriginalDataElementsBuilder` → `IsoSwitch.Domain` (remove `Class1.cs`). Relocate the three port interfaces `IIsoAuditService`, `ISwitchEventPublisher`, `IRoutingEngineV2` → `IsoSwitch.Application` (impls stay in Infra).

**IS-S2 (ARCH-11)**: move 5 transaction handlers + `ConnectorRegistry` + `IsoConnectorConfig` + integration-event records + the DTOs at the bottom of `Program.cs` → `IsoSwitch.Application`. Extend MediatR scan (line 92) to the Application marker. **Boot tests gate a missed scan.**

**IS-S3 (ARCH-12)**: `ConfigSyncConsumer` → existing `IsoSwitch.Infrastructure.Consumers` (do NOT create — it exists); `DbMigrateWorker` + concrete `IIsoAuditService`/`ISwitchEventPublisher` impls → `Infrastructure.Persistence` / their Infra home. These Infra projects reference `Application` to see the ports.

**IS-S4 (ARCH-13)**: thin `Program.cs`; `public partial class Program {}` (line 328) preserved. **Codec separation strategy**: the dev/demo codec is co-owned by `TcpIso8583Server` and STAYS in `Api`; the primary spec codec stays in its current home. They are never merged into one file/class (ARCH-PRES-3) — IS-S4 touches neither codec, only moves handlers/registry/consumers out.

## 5. MediatR and DI Wiring

Define a marker `public sealed class ApplicationMarker {}` in each Application assembly (any public type suffices). Handlers are discovered by assembly scan: `AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ApplicationMarker>())`. This works because MediatR reflects over the *assembly containing the marker type* and auto-registers every `IRequestHandler<,>` it finds — so once handlers physically live in the Application assembly, pointing the scan at that assembly registers them all. Today both `Program.cs` scan `typeof(Program).Assembly` (CV line 60, IS line 92); the scan switch lands in the SAME slice as the handler move (CV-S2+S3, IS-S2) or handlers vanish from DI and boot tests fail loud.

## 6. Port Definitions

| Port | Service | Declared in | Implemented in |
|------|---------|-------------|----------------|
| `ICardVaultDbContext` (new, narrow) | CardVault | `Application` | `Infrastructure.Persistence` (`CardVaultDbContext` implements it) |
| `IIsoAuditService` (relocated) | IsoSwitch | `Application` | `Infrastructure.Persistence` (or audit Infra home) |
| `ISwitchEventPublisher` (relocated) | IsoSwitch | `Application` | `Infrastructure.*` (Kafka/event home) |
| `IRoutingEngineV2` (relocated) | IsoSwitch | `Application` | its existing Infra impl home |

CardVault notification/audit emission stay concrete (services move wholesale; no port — ARCH-5 minimalism). No port has an impl inside its own Application project (ARCH-5, ARCH-10).

## 7. Test Strategy

Tests pass without rewriting because **concrete services move WITH their callers** — a test that `new`s up `BillingService` still `new`s the same class; only its namespace changed (ARCH-INV-4). Two `using`-update touch-points: (a) ~56 CardVault test files referencing `CardVault.Api.{Services,Features,Domain enums}` → `CardVault.Application.*` / `CardVault.Domain` (global find/replace, lands atomically with each move; missing one is `CS0246` at compile); (b) IsoSwitch handler/domain tests → `IsoSwitch.Application.*` / `IsoSwitch.Domain`. Coverage preserved = exactly **650 green, 0 failed, exit 0** after every slice (ARCH-TEST-1); no skips/deletions (ARCH-INV-3).

## 8. Migration Checklist per Slice

For EACH slice: (1) move the cohesive group + rewrite its `namespace`; (2) global `using` rename across src + test files; (3) add/remove `<ProjectReference>` and (CV-S2+S3) `<FrameworkReference>`; (4) update `Program.cs` DI/MediatR scan if the slice moves handlers or introduces a port; (5) `dotnet build` proves references point inward, no cycle; (6) `dotnet test` = 650 green BEFORE commit. Revert = revert the slice's commit(s); no data/schema/runtime change to undo.

## 9. Risks and Mitigations

| Hazard | Handling |
|--------|----------|
| **Reference cycle** (`Application` needs `CardVaultDbContext` in `Persistence`, which already references `Application`) | ADR-6: `ICardVaultDbContext` port in Application; impl in Persistence; existing `Persistence → Application` ref now carries the interface. NEVER add `Application → Infrastructure`. |
| Service-locator in `HoldService`/`SwitchTxnConsumer` | Namespace move only; `IServiceProvider` usage and type-based registrations unchanged (ARCH-INV-6). |
| Fragile IsoSwitch static store init order | Preserve `await BinRoutingStore.InitializeFromDbAsync` (line 190) + `PanMapStore` (line 191) verbatim; no reorder/conditional. (Spec lists a 3rd `IsoTraceStore` call — only 2 exist in code; preserve what exists, do not invent.) |
| Circular-ref pitfalls moving types | Per-slice `dotnet build` after each move; resolve any cross-layer concrete dep with a narrow Application port, never an inward-violating reference. |
| MediatR scan miss | Scan switch in the same slice as the handler move; boot tests fail loud. |
| `.cshtml` not copied | Move both `ItemGroup`s (source + test link) in CV-S5; verify file lands in test output dir. |
| `public partial class Program {}` removed → `WebApplicationFactory<Program>` breaks | Preserved in both Apis (CV 575, IS 328) through every slice (ARCH-PRES-1). |

## 10. Alternatives Considered

- **Merge `Infrastructure.Persistence` + `Infrastructure.Identity`?** No — the fine-grained split is real and credible; merging loses the conventional layout for zero gain (ARCH-INV-5). Only ADD Messaging/Notifications.
- **Move entities to Domain?** No — that is DDD aggregate modeling + a mapping layer = a rewrite, deferred to Ola 4+ (ADR-4, ARCH-INV-1). EF entities stay in Persistence; Domain holds only enums + pure calcs.
- **Keep IsoSwitch codec split?** Yes — dev/demo codec is co-owned by `TcpIso8583Server` (Api host concern); the production spec codec is a separate stack. Merging them couples demo wiring to the prod pipeline (ARCH-PRES-3).
- **Keep enums in their old namespace (prior draft ADR-5)?** Rejected — physical ownership in Domain with a phantom Infrastructure namespace misrepresents the layout to the very reviewer this change targets. A `using CardVault.Domain;` rename is cheap and honest.

## Open Questions
- [ ] Confirm `ICardVaultDbContext` surface = the exact `DbSet<>`s + `SaveChangesAsync` overloads the moved services touch (derive mechanically in CV-S2+S3; keep it the narrowest compiling interface).
- [ ] `ApplicationMarker` placement: a dedicated empty public class per Application assembly (recommended) vs reusing an existing public type.
