# Delta Spec — Ola 2: Populate Clean Architecture Layers (CardVault + IsoSwitch)
# Capability: clean-architecture-layers
# Change: ola2-cardvault-clean-architecture
# Base spec: (no prior base spec — this change introduces structural/architectural SHALL constraints
#             for the CardVault and IsoSwitch backend services)

This document records ONLY what changes. It describes the WHAT (structural contracts), not the HOW
(implementation mechanics). Unchanged product behaviors are not repeated here.

This is a **behavior-preserving refactor**. There are NO new product behaviors and NO modified product
specs. All requirements below are expressed as structural and architectural assertions — what MUST be
true about project layout, dependency direction, and test integrity after the change is applied.

---

## Scope Note

All deliverables in this change are code-reorganization moves between .NET projects within the same
solution (`backend/CardSwitchPlatform.sln`). No application behavior changes. No new features. No bug
fixes bundled in. Requirements are expressed as:

- **Structural membership constraints** — which types MUST live in which project.
- **Dependency direction constraints** — which project references are forbidden.
- **Test-integrity constraints** — the 650-test suite MUST remain green after every slice.
- **Preservation constraints** — specific fragile constructs that MUST NOT be disturbed.

---

## ADDED Requirements

---

### Requirement ARCH-1: CardVault.Domain — Platform Enums

`CardVault.Domain` SHALL contain all platform-wide enum types previously held in
`CardVault.Infrastructure.Persistence` (or `CardVault.Api`):

- `CardStatus`
- `AccountType`
- `AccountStatus`
- `LedgerEntryType`
- `FeeType`
- `StatementStatus`

No placeholder file (`Class1.cs`) SHALL remain in `CardVault.Domain` after slice CV-S1 is applied.

#### Scenario: CardVault.Domain compiles with enum types present

- GIVEN the CV-S1 slice has been applied
- WHEN `dotnet build backend/CardSwitchPlatform.sln` is executed
- THEN `CardVault.Domain` compiles successfully and its assembly contains the enum types listed above
- AND no `Class1.cs` placeholder type exists in the `CardVault.Domain` assembly

#### Scenario: Infrastructure.Persistence references Domain for enums

- GIVEN the CV-S1 slice has been applied
- WHEN the `CardVault.Infrastructure.Persistence.csproj` is inspected
- THEN it contains a `<ProjectReference>` to `CardVault.Domain`
- AND entity type declarations referencing the platform enums compile without error

---

### Requirement ARCH-2: CardVault.Domain — Pure Billing and Switch Calculators

`CardVault.Domain` SHALL contain the following pure, side-effect-free calculation types, moved from
their previous location in `CardVault.Api`:

- `MinimumPaymentService.CalculateMinimum()` (or an equivalent domain type encapsulating the logic)
- `BillingService.ApplyClosingTotals()` (ADR-6 formula)
- `BillingService.ComputeAverageDailyBalance()`
- `HoldService.MapResponseCode()` (ISO 8583 DE39 response-code mapping)

These types SHALL be pure (no I/O, no EF DbContext dependency, no DI service-locator calls).

#### Scenario: Pure calculators compile in Domain with no infrastructure dependency

- GIVEN the CV-S1 slice has been applied
- WHEN `CardVault.Domain.csproj` is inspected for `<ProjectReference>` entries
- THEN it contains NO reference to `CardVault.Infrastructure.Persistence`,
  `CardVault.Infrastructure.Identity`, or `CardVault.Api`
- AND the assembly containing the pure calculators compiles successfully

---

### Requirement ARCH-3: CardVault.Application — CQRS Handlers

`CardVault.Application` SHALL contain all CQRS feature handlers previously under
`CardVault.Api.Features.*` (approximately 31 handler files covering the full feature surface of the
service).

No handler type from the `CardVault.Api.Features.*` namespace SHALL remain in the `CardVault.Api`
project after slice CV-S2+S3 is applied.

The MediatR assembly scan in `CardVault.Api`'s `Program.cs` SHALL include the
`CardVault.Application` assembly (e.g., `AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ApplicationMarker>())`).

#### Scenario: MediatR resolves handlers from Application assembly

- GIVEN the CV-S2+S3 slice has been applied
- WHEN `dotnet test backend/CardSwitchPlatform.sln` is executed
- THEN all CardVault integration/boot tests that dispatch MediatR commands pass
- AND no "handler not found" or "no handler registered" exception is thrown

#### Scenario: Api project contains no handler types

- GIVEN the CV-S2+S3 slice has been applied
- WHEN the `CardVault.Api` assembly is inspected (e.g., via `dotnet-symbol`, reflection, or source-level grep)
- THEN no type whose name ends with `Handler` or `CommandHandler` or `QueryHandler` exists inside
  the `CardVault.Api` project's own source tree (excluding generated/startup scaffolding)

---

### Requirement ARCH-4: CardVault.Application — Business Services

`CardVault.Application` SHALL contain all business-logic services previously under
`CardVault.Api.Services` (approximately 30 service files, excluding services that are purely
infrastructure adapters — i.e., those that directly manage external I/O without business logic).

No business service type from `CardVault.Api.Services` SHALL remain in the `CardVault.Api` project
after slice CV-S2+S3 is applied.

#### Scenario: Business services compile in Application with correct references

- GIVEN the CV-S2+S3 slice has been applied
- WHEN `dotnet build backend/CardSwitchPlatform.sln` is executed
- THEN `CardVault.Application` compiles with all relocated business services
- AND the full 650-test suite passes

---

### Requirement ARCH-5: CardVault.Application — Minimal Ports

`CardVault.Application` SHALL define only the port interfaces (abstractions) that the handler/service
moves strictly require in order to compile — no speculative or aspirational port layer.

Required ports include at minimum:
- A persistence context/repository abstraction sufficient for the relocated handlers.
- `IAuditWriter` (or equivalent) if referenced by relocated handlers.
- A notification emission abstraction if referenced by relocated services.

Port interfaces in `CardVault.Application` SHALL NOT have concrete implementations inside the
`CardVault.Application` project itself — implementations belong in `Infrastructure` or `Api`.

#### Scenario: Application ports have no Infrastructure implementation inside Application

- GIVEN the CV-S2+S3 slice has been applied
- WHEN `CardVault.Application.csproj` is inspected
- THEN it contains NO `<ProjectReference>` to any `Infrastructure.*` project
- AND it contains NO `<ProjectReference>` to `CardVault.Api`

---

### Requirement ARCH-6: CardVault.Infrastructure.Messaging — Kafka Adapters

A project (new or existing) SHALL host the Kafka consumer and publisher adapters previously in
`CardVault.Api`:

- `SwitchTxnConsumer`
- `AuthDecisionPublisher`

After slice CV-S4, neither `SwitchTxnConsumer` nor `AuthDecisionPublisher` SHALL exist in the
`CardVault.Api` source tree.

#### Scenario: Kafka adapters compile in Infrastructure.Messaging

- GIVEN the CV-S4 slice has been applied
- WHEN `dotnet build backend/CardSwitchPlatform.sln` is executed
- THEN the infrastructure project hosting the Kafka adapters compiles without error
- AND the 650-test suite remains green

---

### Requirement ARCH-7: CardVault.Infrastructure.Notifications — Notification Adapters

A project (new or existing) SHALL host the notification providers, Razor templates, and webhook
adapters previously under `CardVault.Api.Services/Notifications/**`.

After slice CV-S5:
- No notification provider, Razor template, or webhook adapter type SHALL remain in `CardVault.Api`.
- Razor `.cshtml` template files SHALL retain a `<CopyToOutputDirectory>` build action so the runtime
  can locate them.
- If `CardVault.Tests` references notification template files via an `<ItemGroup>` path, that path
  SHALL be updated to the new assembly location.

#### Scenario: Notification templates are present at runtime after relocation

- GIVEN the CV-S5 slice has been applied and the solution is built
- WHEN the test or runtime process loads notification templates
- THEN templates resolve at their expected output-directory path
- AND no `FileNotFoundException` for `.cshtml` templates is thrown

---

### Requirement ARCH-8: CardVault.Api — Thin Composition Root

After slice CV-S6, `CardVault.Api` SHALL contain ONLY:
- `Program.cs` — DI wiring, middleware pipeline, startup configuration.
- Controllers and/or Minimal API endpoint definitions.
- HTTP-layer DTOs (`Contracts/` or equivalent request/response models).
- Host-specific concerns: PCI boundary, security middleware, vault integration, HTTP certificate
  handling.

`CardVault.Api` SHALL NOT contain any of the following after CV-S6:
- CQRS feature handlers.
- Business-logic services.
- Kafka consumer/publisher adapters.
- Notification provider implementations or Razor templates.

#### Scenario: Api project source tree contains only HTTP and host concerns

- GIVEN the CV-S6 slice has been applied
- WHEN the `CardVault.Api` source tree is inspected
- THEN no handler, business service, Kafka adapter, or notification provider type appears in its
  source files
- AND `dotnet build` and `dotnet test` both pass with 650 green tests

---

### Requirement ARCH-9: IsoSwitch.Domain — State Machine, Constants, Pure Logic

`IsoSwitch.Domain` SHALL contain:
- `TransactionStateMachine`
- `TransactionTypes` and `TransactionStatuses` (or equivalent transaction-lifecycle constants)
- `OriginalDataElementsBuilder` (pure ISO 8583 field construction logic)

No placeholder file (`Class1.cs`) SHALL remain in `IsoSwitch.Domain` after slice IS-S1 is applied.

#### Scenario: IsoSwitch.Domain compiles with domain types present

- GIVEN the IS-S1 slice has been applied
- WHEN `dotnet build backend/CardSwitchPlatform.sln` is executed
- THEN `IsoSwitch.Domain` compiles and its assembly contains the types listed above
- AND no `Class1.cs` placeholder type exists in the `IsoSwitch.Domain` assembly

---

### Requirement ARCH-10: IsoSwitch.Application — Port Interfaces

`IsoSwitch.Application` SHALL define the following port interfaces:
- `IIsoAuditService`
- `ISwitchEventPublisher`
- `IRoutingEngineV2`

Concrete implementations of these interfaces SHALL NOT reside in `IsoSwitch.Application` — they
belong in their respective Infrastructure projects.

#### Scenario: IsoSwitch.Application contains port interfaces with no concrete implementations

- GIVEN the IS-S1 slice has been applied
- WHEN `IsoSwitch.Application.csproj` is inspected
- THEN it contains a `<ProjectReference>` to `IsoSwitch.Domain`
- AND it contains NO reference to any `IsoSwitch.Infrastructure.*` project
- AND it contains NO reference to `IsoSwitch.Api`

---

### Requirement ARCH-11: IsoSwitch.Application — Handlers, ConnectorRegistry, MediatR Scan

`IsoSwitch.Application` SHALL contain all 5 transaction command handlers previously under
`IsoSwitch.Api.Features.*`, plus:
- `ConnectorRegistry`
- `IsoConnectorConfig`
- Integration-event record types
- Application-layer DTOs previously embedded at the bottom of `IsoSwitch.Api/Program.cs`

The MediatR assembly scan in `IsoSwitch.Api`'s `Program.cs` SHALL include the `IsoSwitch.Application`
assembly.

#### Scenario: IsoSwitch MediatR resolves handlers from Application assembly

- GIVEN the IS-S2 slice has been applied
- WHEN `dotnet test backend/CardSwitchPlatform.sln` is executed
- THEN all IsoSwitch boot/integration tests that dispatch MediatR commands pass
- AND no "handler not found" exception is thrown

---

### Requirement ARCH-12: IsoSwitch Infrastructure — Extracted Adapters

After slice IS-S3:
- `ConfigSyncConsumer` SHALL reside in `IsoSwitch.Infrastructure.Consumers` (or equivalent
  infrastructure project for consumers).
- `DbMigrateWorker` SHALL reside in `IsoSwitch.Infrastructure.Persistence`.
- Concrete implementations of `IIsoAuditService` and `ISwitchEventPublisher` SHALL reside in their
  respective Infrastructure projects.

None of the above types SHALL remain in `IsoSwitch.Api`'s source tree after IS-S3.

#### Scenario: IsoSwitch infrastructure adapters compile in their designated projects

- GIVEN the IS-S3 slice has been applied
- WHEN `dotnet build backend/CardSwitchPlatform.sln` is executed
- THEN all relocated IsoSwitch infrastructure types compile in their new homes
- AND the 650-test suite remains green

---

### Requirement ARCH-13: IsoSwitch.Api — Thin Composition Root

After slice IS-S4, `IsoSwitch.Api` SHALL contain ONLY:
- `Program.cs` — DI wiring and startup.
- Endpoint definitions (Minimal API or Controllers).
- The API-layer (dev/demo) codec stack — `TcpIso8583Server` and any associated dev codec types.
  This codec is co-owned by the API layer and MUST stay in `Api`.

#### Scenario: IsoSwitch.Api source tree contains only HTTP/TCP surface and DI wiring

- GIVEN the IS-S4 slice has been applied
- WHEN the `IsoSwitch.Api` source tree is inspected
- THEN no transaction handler, `ConnectorRegistry`, consumer, or audit/publisher implementation
  appears in its source files
- AND the dev/demo TCP codec types are still present in `IsoSwitch.Api`
- AND `dotnet build` and `dotnet test` pass with 650 green tests

---

## Dependency Direction Requirements

---

### Requirement ARCH-DEP-1: Domain Has No Outward References

`CardVault.Domain` and `IsoSwitch.Domain` SHALL contain NO `<ProjectReference>` to any of the
following from their own service:
- `*.Application`
- `*.Infrastructure.*`
- `*.Api`

#### Scenario: Domain project references are inward-only

- GIVEN any slice has been applied
- WHEN `CardVault.Domain.csproj` and `IsoSwitch.Domain.csproj` are inspected
- THEN neither file contains a `<ProjectReference>` pointing to an Application, Infrastructure,
  or Api project within the same service
- AND `dotnet build` completes without circular reference errors

---

### Requirement ARCH-DEP-2: Application Does Not Reference Api (Persistence reference is a documented, time-bounded exception)

`CardVault.Application` and `IsoSwitch.Application` SHALL contain NO `<ProjectReference>` to the same
service's `*.Api` project. This is absolute — the Api is the composition root and nothing below it may
depend on it.

`Application` MAY reference `Domain` and shared cross-service libraries (e.g., `BuildingBlocks`).

**Documented exception — `Application → Infrastructure.Persistence` (decided during CV-S2+S3 apply):**
Because the EF entity types remain the shared model living in `Infrastructure.Persistence` (DDD aggregates
are explicitly deferred to Ola 4+), the relocated handlers and services reference those entity types
directly. A `DbContext` port (`ICardVaultDbContext`) was evaluated and **rejected**: it abstracts the
context but NOT the entity types, so it does not remove the reference and would only add speculative
layering the proposal forbade. Therefore `CardVault.Application` (and, if IS-S2 confirms the same
constraint, `IsoSwitch.Application`) MAY reference its same-service `Infrastructure.Persistence` project
SOLELY to consume the shared EF entity model. The dead, unused `Infrastructure.Persistence → Application`
reference is removed to keep the direction one-way (`Application → Persistence`, no cycle). This compromise
is deliberate and time-bounded: it is revisited when entities move to a true domain model in Ola 4+.

#### Scenario: Application does not reference Api, and the reference graph is acyclic

- GIVEN all CardVault slices have been applied
- WHEN `CardVault.Application.csproj` is inspected
- THEN its `<ProjectReference>` entries include `CardVault.Domain`, `CardVault.Infrastructure.Persistence` (shared entity model), and optionally `BuildingBlocks`
- AND NO entry references `CardVault.Api`
- AND `CardVault.Infrastructure.Persistence` does NOT reference `CardVault.Application` (one-way only)
- AND `dotnet build` succeeds with no circular-reference error

---

### Requirement ARCH-DEP-3: Api References Application and Infrastructure (not vice versa)

The dependency direction in each service SHALL be:

```
Api → Application → Domain
 ↓
Infrastructure.*
```

`Api` is the only layer that may reference both `Application` and `Infrastructure.*`. No layer below
`Api` in the hierarchy SHALL reference `Api`.

#### Scenario: Build succeeds with the correct reference graph

- GIVEN all slices for a service have been applied
- WHEN `dotnet build backend/CardSwitchPlatform.sln` is executed
- THEN the build succeeds without circular reference errors
- AND the reference graph of each service matches the inward-pointing hierarchy above

---

## Test-Integrity Requirements

---

### Requirement ARCH-TEST-1: 650 Tests Green After Every Slice

The full test suite (`dotnet test backend/CardSwitchPlatform.sln`) SHALL pass after EACH of the
following slices is committed:

| Slice   | Description                                              | Expected green |
|---------|----------------------------------------------------------|----------------|
| CV-S1   | CardVault Domain: enums + pure calculators               | 650            |
| CV-S2+S3| CardVault Application: handlers + services + ports + MediatR scan | 650   |
| CV-S4   | CardVault Infrastructure.Messaging: Kafka adapters       | 650            |
| CV-S5   | CardVault Infrastructure.Notifications: notification adapters | 650       |
| CV-S6   | CardVault Api: thin composition root                     | 650            |
| IS-S1   | IsoSwitch Domain + Application ports                     | 650            |
| IS-S2   | IsoSwitch Application handlers + ConnectorRegistry + MediatR scan | 650  |
| IS-S3   | IsoSwitch Infrastructure extraction                      | 650            |
| IS-S4   | IsoSwitch Api cleanup                                    | 650            |

The 650 tests break down as: CardVault 579, IsoSwitch 53, IsoAudit 18.

#### Scenario: Test suite stays green after each slice commit

- GIVEN a slice has been committed
- WHEN `dotnet test backend/CardSwitchPlatform.sln` is executed
- THEN the output reports exactly 650 tests passed and 0 failed
- AND the exit code is 0

#### Scenario: Using directives in test files are updated atomically

- GIVEN a type is moved from `CardVault.Api.Services` to `CardVault.Application.Services`
- WHEN the test files that reference that type are inspected
- THEN each `using` directive references the new namespace (`CardVault.Application.Services`)
- AND the test project compiles without `CS0246` (type not found) errors

---

## Preservation Requirements

---

### Requirement ARCH-PRES-1: public partial class Program{} Preserved in Both Api Projects

`CardVault.Api/Program.cs` and `IsoSwitch.Api/Program.cs` SHALL each contain the declaration:

```csharp
public partial class Program { }
```

This declaration SHALL be present after every slice — including all intermediate slices where
`Program.cs` is modified for DI/MediatR scan updates.

#### Scenario: WebApplicationFactory tests remain compilable through all slices

- GIVEN any slice that modifies `Program.cs` has been applied
- WHEN the test project is compiled
- THEN types that reference `WebApplicationFactory<Program>` compile without error
- AND the `public partial class Program {}` declaration is present in `Program.cs`

---

### Requirement ARCH-PRES-2: IsoSwitch Global Static Store Startup Init Order Preserved

The IsoSwitch `Program.cs` startup block SHALL preserve the initialization calls for the global
static stores in their existing order:

- `BinRoutingStore.InitializeFromDbAsync(...)`
- `PanMapStore.InitializeFromDbAsync(...)`
- `IsoTraceStore.InitializeFromDbAsync(...)`

These calls SHALL NOT be removed, reordered, or made conditional as a side effect of any slice.

#### Scenario: Static store initialization remains in Program.cs after IS-S4

- GIVEN the IS-S4 slice has been applied
- WHEN `IsoSwitch.Api/Program.cs` is inspected
- THEN `await BinRoutingStore.InitializeFromDbAsync(...)`, `await PanMapStore.InitializeFromDbAsync(...)`,
  and `await IsoTraceStore.InitializeFromDbAsync(...)` all appear in the startup block
- AND the order is preserved relative to the pre-change baseline

---

### Requirement ARCH-PRES-3: IsoSwitch Dual Codec Separation Preserved

The IsoSwitch service uses two codec stacks:
- **Primary spec codec** — used by the production ISO 8583 message pipeline.
- **Dev/demo codec** — co-owned by `TcpIso8583Server`, used for development/demo TCP traffic;
  MUST remain in `IsoSwitch.Api`.

These two codec stacks SHALL NOT be merged. After slice IS-S4:
- The dev/demo codec types and `TcpIso8583Server` SHALL remain in `IsoSwitch.Api`.
- The primary spec codec SHALL remain in its pre-change home.

#### Scenario: Dev codec remains in Api; primary codec remains separate

- GIVEN the IS-S4 slice has been applied
- WHEN `IsoSwitch.Api` and the primary codec source location are inspected
- THEN `TcpIso8583Server` and its associated dev codec types exist in `IsoSwitch.Api`
- AND the primary production codec is NOT merged into the same file or class as the dev codec

---

### Requirement ARCH-PRES-4: No Behavior Change, Bug Fix, or Feature Bundled

The diff across all slices SHALL consist ONLY of:
- Type moves between .NET projects.
- Namespace declarations updated to match the new project.
- `using` directive updates in consuming files.
- `<ProjectReference>` additions/removals in `.csproj` files.
- `Program.cs` DI registration and MediatR scan updates reflecting the new assembly locations.
- `.csproj` build-action entries (e.g., `CopyToOutputDirectory`) updated for moved files.

The diff SHALL NOT contain:
- Logic changes inside moved types.
- New product behavior, endpoints, or features.
- Bug fixes (discovered bugs are logged separately and deferred).
- Performance optimizations (no query changes, no async/threading changes beyond what a move requires).

#### Scenario: Moved types have identical logic before and after the move

- GIVEN any type that has been moved between projects
- WHEN the method bodies and property implementations of that type are diffed against the pre-change
  baseline
- THEN no logic change is present (only namespace declarations and `using` directives differ)

---

## Invariants (SHALL NOT Change)

The following MUST remain true throughout all slices and at the end of the change:

- **ARCH-INV-1**: EF entity types remain in `Infrastructure.Persistence` with their navigation
  properties and data annotations intact. No entity is moved to `Domain` or split into a domain
  aggregate in this change.
- **ARCH-INV-2**: `IsoAudit.Api` is not modified. It has no `Domain`/`Application` projects and is
  explicitly out of scope for Ola 2.
- **ARCH-INV-3**: All 650 tests (CardVault 579, IsoSwitch 53, IsoAudit 18) pass on `main` at the
  end of the change with no skips, ignores, or test deletions.
- **ARCH-INV-4**: No test is rewritten to use interfaces/mocks where it previously instantiated a
  concrete service directly — test rewriting is out of scope.
- **ARCH-INV-5**: The `CardVault.Infrastructure.Persistence` and `CardVault.Infrastructure.Identity`
  projects are NOT merged. The existing fine-grained split is preserved; only `Infrastructure.Messaging`
  and `Infrastructure.Notifications` are added as new projects.
- **ARCH-INV-6**: `HoldService` and `SwitchTxnConsumer` resolve services via `IServiceProvider` —
  this service-locator pattern is NOT refactored; it moves unchanged.

---

## Out-of-Scope Confirmations

The following are explicitly NOT requirements of this change:

- **True DDD domain model** — separate domain aggregates/value objects distinct from EF entities,
  plus a persistence↔domain mapping layer. Deferred to **Ola 4+**. EF entities remain the shared
  model across all layers in Ola 2.
- **IsoAudit refactor** — `IsoAudit.Api` is a minimal standalone service. It is not touched.
- **New ports/mocks in tests** — tests continue to instantiate concrete services directly. No test
  refactoring toward interface-based mocking.
- **CardVault entity reorganization** — entities stay in `Infrastructure.Persistence` with their
  navigation properties and annotations intact.
- **Performance work** — no query optimization, caching, or async/threading changes.
- **Any behavior change, bug fix, or feature** — behavior is 100% preserved.
