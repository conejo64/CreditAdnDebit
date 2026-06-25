# Proposal: Ola 2 — Populate Clean Architecture Layers (CardVault + IsoSwitch)

## Intent

Ola 1 made the platform buildable and shippable; it is no longer a literal prototype. But it still **looks** like one where it matters most for a sale: the architecture is hollow. In both backend services that carry the product's value — `CardVault` (the issuing/credit engine) and `IsoSwitch` (the ISO 8583 switch) — the `Domain` and `Application` projects contain nothing but a `Class1.cs` placeholder. Every enum, every business rule, every CQRS handler, every service lives inside the `*.Api` project. The Clean Architecture layout is real on paper (project references already point inward: `Api → Application → Domain`), but it is a facade: the empty projects exist only to imply a structure that the code does not honor.

This is the most important remaining piece of **Gate 1 — "stop being a prototype"** — and the driver is **due-diligence credibility**, not runtime behavior. The buyer is an Ecuadorian cooperative or fintech doing technical due diligence on a card credit/debit platform. When their reviewer opens the solution and sees `CardVault.Domain` and `CardVault.Application` holding a single empty placeholder class while a 142-file `CardVault.Api` does everything, they distrust the entire codebase — and rightly so. Empty architecture projects are worse than no architecture projects: they signal that someone built the scaffolding to look professional and never filled it in.

This change **populates the layers**. It moves enums and pure logic into `Domain`, services and CQRS handlers and ports into `Application`, messaging/notification adapters into `Infrastructure`, and leaves each `*.Api` as a thin composition root + HTTP surface. It is a **pragmatic, behavior-preserving refactor** — code moves between projects, namespaces and `using` directives change, DI and MediatR assembly scans are adjusted — but **no behavior changes**. The 650 existing tests (CardVault 579, IsoSwitch 53, IsoAudit 18) are the safety net and MUST stay green after every slice.

**Honest expectation-setting (read this before scoping):** This is "populate the layers," NOT "introduce a true DDD domain model." The EF entities in `Infrastructure.Persistence` REMAIN the shared model used across layers. We are NOT creating separate domain aggregates, value objects, or a mapping layer between persistence and domain — that is a rewrite, not a refactor, and it is **explicitly out of scope and deferred to a future change (Ola 4+)**. After Ola 2, a reviewer sees real, organized code in `Domain` and `Application` with the dependency direction enforced inward — a credible, conventional .NET Clean Architecture layout — while we remain honest that the entities are still the shared model.

**Success looks like:**
- `CardVault.Domain` and `CardVault.Application` (and the IsoSwitch equivalents) contain real, meaningful code — not placeholders.
- The dependency direction is enforced inward at the code level, not just via project references.
- All 650 tests are green after every slice and at the end.
- Each `*.Api` is thinned to composition + HTTP concerns (Program.cs wiring, Controllers/Endpoints, HTTP DTOs).

## Scope

### In Scope

- **CardVault — populate the layers (behavior-preserving):**
  - Move enums (`CardStatus`, `AccountType`, `AccountStatus`, `LedgerEntryType`, `FeeType`, `StatementStatus`, etc.) from `Infrastructure.Persistence` into `CardVault.Domain`. This is the single highest-value move — every service uses these enums.
  - Move pure value/calculation logic into `CardVault.Domain`: `MinimumPaymentService.CalculateMinimum()`, `BillingService.ApplyClosingTotals()` (ADR-6 formula), `BillingService.ComputeAverageDailyBalance()`, `HoldService.MapResponseCode()` (ISO 8583 DE39 mapping) — each as a static/pure domain type.
  - Move the CQRS handlers in `Features/**` (~31 files) from `CardVault.Api.Features.*` to `CardVault.Application.Features.*`.
  - Move the business services in `Services/**` (~30 files, excluding pure-infrastructure ones) from `CardVault.Api.Services` to `CardVault.Application.Services`.
  - Define the minimal Application ports needed for the moves to compile cleanly (e.g. a context/repository abstraction, `IAuditWriter`, notification emission) — only as far as behavior preservation requires, not as a speculative port layer.
  - Move messaging adapters (`SwitchTxnConsumer`, `AuthDecisionPublisher`) and notification infrastructure (`Services/Notifications/**` providers, templates, webhooks) into the appropriate `Infrastructure` home (existing or a new `Infrastructure.Messaging` / `Infrastructure.Notifications` project).
  - Update `Program.cs` DI registrations and the MediatR assembly scan (`AddMediatR(... typeof(ApplicationMarker).Assembly)`).
  - Update the ~56 test files' `using` directives **atomically with each move** so the suite stays green.
- **IsoSwitch — populate the layers (same approach, after CardVault):**
  - Move `TransactionStateMachine`, `TransactionTypes`/`TransactionStatuses`, `OriginalDataElementsBuilder` (pure logic) into `IsoSwitch.Domain`.
  - Move the port interfaces `IIsoAuditService`, `ISwitchEventPublisher`, `IRoutingEngineV2` into `IsoSwitch.Application` (implementations stay in their Infrastructure projects).
  - Move the 5 transaction command handlers from `IsoSwitch.Api.Features.*` into `IsoSwitch.Application.Features.*`, plus `ConnectorRegistry`, `IsoConnectorConfig`, the integration-event records, and DTOs from the bottom of `Program.cs`.
  - Move `ConfigSyncConsumer` into `Infrastructure.Consumers`, `DbMigrateWorker` into `Infrastructure.Persistence`, and the concrete audit/publisher implementations into their Infrastructure home.
  - Update the MediatR assembly scan to include the `IsoSwitch.Application` assembly; preserve the global static store startup initialization, the dual codec separation, and `public partial class Program {}`.
- **Sequencing (decided):** ALL CardVault slices land first to establish the proven reference pattern, THEN IsoSwitch slices reuse that approach. One SDD change, staged blast radius — **not** simultaneous.

### Out of Scope

- **True DDD domain model** — separate domain aggregates/value objects distinct from EF entities, plus a persistence↔domain mapping layer. This is a rewrite, deferred to **Ola 4+**. EF entities remain the shared model in this change.
- **IsoAudit refactor** — `IsoAudit.Api` is a minimal standalone service with no Domain/Application projects; it is not a candidate for this change.
- **Any behavior change, bug fix, or feature** — strictly mechanical moves + namespace/using/DI adjustments. No bug fixes bundled in; behavior is preserved.
- **Performance work** — no query optimization, no caching changes, no async/threading rework beyond what a move mechanically requires.
- **Introducing test ports/mocks where none exist** — tests currently instantiate concrete services directly; we do not rewrite them to use interfaces.
- **CardVault entity reorganization** — entities stay in `Infrastructure.Persistence` with their navigation properties and annotations intact.

## Capabilities

### New Capabilities

None at the product/spec level. This change relocates existing code and enforces architecture; it adds no product behavior.

### Modified Capabilities

None functionally. The `sdd-spec` phase will express this change as **structural/architectural requirements** (e.g. "`CardVault.Domain` SHALL contain the platform enums and pure billing calculations", "the `Application` layer SHALL NOT reference the `Api` project", "all 650 tests SHALL remain green after each slice") rather than as changes to existing product specs.

## Approach

Populate the empty layers via **mechanical moves + namespace/using updates + DI/MediatR scan adjustments**, sliced so the full test suite stays green after every slice. The guiding decisions (from exploration):

- **Move-then-adjust-namespaces, not introduce-interfaces-first.** Introducing ports/interfaces ahead of the move is architecturally purer but doubles the file count and would force mocking infrastructure across 56 test files. For a behavior-preserving refactor we move the code, then add only the ports the moves require to compile.
- **Keep the existing fine-grained Infrastructure split.** `Infrastructure.Persistence` and `Infrastructure.Identity` already exist and are real. Do not merge them. Add `Infrastructure.Messaging` / `Infrastructure.Notifications` only when the Kafka/notification adapters actually move.
- **Atomicity is the discipline.** Each move and its `using`/DI/MediatR consequences land together in one slice so the suite never goes red between commits. The churn is overwhelmingly mechanical (`using CardVault.Api.Services` → `using CardVault.Application.Services`), which a global rename handles for ~90% of it.

**CardVault slices (each keeps 650 tests green):**
- **S1 — Domain: enums + pure logic.** Move all enums to `CardVault.Domain` (highest-value move) and the pure calculators (`CalculateMinimum`, `ApplyClosingTotals`, `ComputeAverageDailyBalance`, `MapResponseCode`). `Infrastructure.Persistence` gains a `Domain` reference for the enums.
- **S2+S3 — Application: handlers + services together.** Move `Features/**` handlers and `Services/**` business services from `Api` to `Application` in one coordinated slice (handlers reference services, so splitting them mid-flight breaks compilation). ~85 files, ~56 test `using`-updates. Update the MediatR scan in `Program.cs`.
- **S4 — Infrastructure.Messaging.** Move `SwitchTxnConsumer` and `AuthDecisionPublisher` to their infrastructure home. Small, well-isolated.
- **S5 — Infrastructure.Notifications.** Move `Services/Notifications/**` (providers, Razor templates, webhooks). Maintain `CopyToOutputDirectory` for the `.cshtml` templates and update the test project's template `ItemGroup`.
- **S6 — Thin Api.** `Program.cs` composes from `Application` + `Infrastructure`; Controllers, HTTP DTOs (`Contracts/`), and `Pci`/`Security`/`Vault` host concerns stay in `Api`.

**IsoSwitch slices (after CardVault, mirroring the proven pattern):**
- **IS-S1 — Domain primitives + Application ports.** `TransactionStateMachine`, transaction constants, `OriginalDataElementsBuilder` to `Domain`; `IIsoAuditService`, `ISwitchEventPublisher`, `IRoutingEngineV2` to `Application`.
- **IS-S2 — Application handlers + ConnectorRegistry + MediatR scan.** Move the 5 handlers, `ConnectorRegistry`, event records, DTOs; extend the MediatR scan to the `Application` assembly. **Boot tests gate this** — a missed scan update fails immediately.
- **IS-S3 — Infrastructure extraction.** `ConfigSyncConsumer` → `Infrastructure.Consumers`; `DbMigrateWorker` → `Infrastructure.Persistence`; concrete audit/publisher impls to their Infrastructure home. Preserve global static store startup init order.
- **IS-S4 — Program.cs cleanup.** `Program.cs` becomes thin DI wiring. Preserve `public partial class Program {}` and the dual codec separation.

Verification per slice is `dotnet test` green (650 tests) plus a `dotnet build` with the dependency direction intact. There is no TDD red/green cycle because no production behavior changes — the test suite is a **regression net**, not a spec for new behavior.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `CardVault.Domain` | Populated | Enums + pure billing/switch calculators moved in (S1) |
| `CardVault.Application` | Populated | CQRS handlers + business services + minimal ports moved in (S2+S3) |
| `CardVault.Infrastructure.Persistence` | Modified | Gains `Domain` reference for enums; entities unchanged |
| `CardVault.Infrastructure.Messaging` | New (likely) | Kafka consumer/publisher relocated (S4) |
| `CardVault.Infrastructure.Notifications` | New (likely) | Notification providers/templates/webhooks relocated (S5) |
| `CardVault.Api` | Thinned | Composition root + Controllers + HTTP DTOs + host concerns only (S6) |
| `CardVault.Tests` | Modified | ~56 files' `using` directives updated atomically with moves |
| `IsoSwitch.Domain` | Populated | State machine, constants, ODE builder (IS-S1) |
| `IsoSwitch.Application` | Populated | Handlers, ports, ConnectorRegistry, event records (IS-S1/IS-S2) |
| `IsoSwitch.Infrastructure.Consumers` | Populated | `ConfigSyncConsumer` relocated (IS-S3) |
| `IsoSwitch.Infrastructure.Persistence` | Modified | `DbMigrateWorker` + concrete audit impls relocated (IS-S3) |
| `IsoSwitch.Api` | Thinned | Program.cs wiring + Endpoints + dev TCP codec only (IS-S4) |
| `IsoSwitch.Tests` | Modified | Handler test `using` directives updated; boot tests gate DI/scan |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| ~56 CardVault test files need `using` updates after namespace moves | High | Mechanical global find/replace; land atomically with each move; run `dotnet test` per slice before commit |
| MediatR assembly scan not updated after handlers move → handlers not found at runtime | Medium | Update `AddMediatR(... ApplicationMarker.Assembly)` in the same slice; integration/boot tests catch a miss immediately |
| EF entities are the shared domain+persistence model — no clean entity boundary | High (accepted) | Explicitly out of scope; entities stay in `Infrastructure.Persistence`; honest "Ola 4+" deferral stated up front |
| `HoldService` / `SwitchTxnConsumer` resolve services via `IServiceProvider` (service-locator DI) | Medium | No behavior change needed — namespace move only; keep registrations by type in `Program.cs` |
| Razor `.cshtml` notification templates need `CopyToOutputDirectory`; test project copies them explicitly | Medium | When templates move assemblies, update both the source `csproj` and the test project's template `ItemGroup` |
| IsoSwitch global static stores (`BinRoutingStore`, `PanMapStore`, `IsoTraceStore`) startup init order | Medium | Preserve the `await *.InitializeFromDbAsync(...)` calls in `Program.cs` startup block (no test covers this silently) |
| IsoSwitch dual codec stacks (primary spec codec vs Api dev/demo codec) accidentally merged | Medium | Keep them separate; the demo codec is co-owned by `TcpIso8583Server` and must stay in `Api` |
| `public partial class Program {}` removed → `WebApplicationFactory<Program>` tests break | Low | Preserve it in both Api projects through all slices |
| Scope creep into bug fixes or DDD aggregates during the move | Medium | Hard constraint: behavior-preserving only; any discovered bug is logged, not fixed in this change |
| `WebApplicationFactory<Program>` breakage from moving types out of Api | Low | `Program` stays in `Api`; moving other types out does not affect the factory |

## Rollback Plan

Every slice is an isolated, behavior-preserving move with the test suite as the gate, so rollback is low-risk and per-slice:

- Each slice is one (or a few) commits that move code and update `using`/DI/scan together. Revert the slice's commit(s) to return to the prior green state — no data migration, no schema change, no runtime behavior change to undo.
- Because behavior is preserved and the 650 tests gate every slice, a reverted slice returns the build to a known-green configuration.
- CardVault slices land before IsoSwitch, so an IsoSwitch problem never destabilizes the already-landed CardVault work.

## Dependencies

- Ola 1 is merged (green CI, buildable/shippable services) — this change relies on the CI test gate to prove the 650 tests stay green per slice.
- No external/infra provisioning, no acquirer/cert coordination, no cloud account.
- CardVault slices are a prerequisite for IsoSwitch slices (the proven reference pattern), but both live in this single change.

## Suggested Slicing (CardVault first, then IsoSwitch)

1. **CV-S1** — Domain: enums + pure calculators.
2. **CV-S2+S3** — Application: CQRS handlers + business services + minimal ports + MediatR scan.
3. **CV-S4** — Infrastructure.Messaging (Kafka consumer/publisher).
4. **CV-S5** — Infrastructure.Notifications (providers/templates/webhooks).
5. **CV-S6** — Thin Api to composition + HTTP.
6. **IS-S1** — IsoSwitch Domain primitives + Application ports.
7. **IS-S2** — IsoSwitch Application handlers + ConnectorRegistry + MediatR scan.
8. **IS-S3** — IsoSwitch Infrastructure extraction (Consumers, Persistence workers, audit impls).
9. **IS-S4** — IsoSwitch Program.cs cleanup.

## Success Criteria

- [ ] `CardVault.Domain` contains the platform enums and pure billing/switch calculations — no `Class1.cs` placeholder.
- [ ] `CardVault.Application` contains the CQRS handlers and business services with only the ports the moves require.
- [ ] `IsoSwitch.Domain` and `IsoSwitch.Application` contain real code (state machine/constants/ODE builder; handlers, ports, ConnectorRegistry) — no placeholders.
- [ ] The dependency direction is enforced inward at the code level: `Application` does not reference `Api`; `Domain` references neither.
- [ ] Messaging/notification adapters live in `Infrastructure`; concrete IsoSwitch audit/publisher/consumer impls live in their Infrastructure projects.
- [ ] Both `*.Api` projects are thinned to composition root + HTTP/endpoints + host concerns.
- [ ] All 650 tests (CardVault 579, IsoSwitch 53, IsoAudit 18) are green after every slice and at the end.
- [ ] `public partial class Program {}`, the IsoSwitch global-static-store startup init, and the dual codec separation are preserved.
- [ ] No behavior change, bug fix, or feature is bundled in — the diff is moves + namespace/using/DI/scan adjustments only.
- [ ] The proposal documents that true DDD aggregates + a persistence↔domain mapping layer are deferred to Ola 4+, keeping buyer expectations honest.
