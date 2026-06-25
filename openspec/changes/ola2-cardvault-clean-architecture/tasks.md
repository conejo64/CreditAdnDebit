# Tasks: Ola 2 — Clean Architecture Layers (CardVault + IsoSwitch)
# Change: ola2-cardvault-clean-architecture

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 1,800–2,400 (moves + namespace renames + .csproj edits + using updates across ~56 test files) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | 9 sequential slices, each a standalone PR: CV-S1 → CV-S2+S3 → CV-S4 → CV-S5 → CV-S6 → IS-S1 → IS-S2 → IS-S3 → IS-S4 |
| Delivery strategy | ask-on-risk (cached from session) |
| Chain strategy | pending (user to confirm stacked-to-main or feature-branch-chain before apply) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| CV-S1 | CardVault Domain: enums + pure calculators | PR 1 | Base: main; self-contained enum extraction + Class1.cs removal |
| CV-S2+S3 | CardVault Application: handlers + services + MediatR scan | PR 2 | Largest slice; depends on PR 1 (enums in Domain). No DbContext port — Application references Persistence directly (ARCH-DEP-2 exception) |
| CV-S4 | CardVault Infrastructure.Messaging: Kafka adapters | PR 3 | New project creation; depends on PR 2 (Application types) |
| CV-S5 | CardVault Infrastructure.Notifications | PR 4 | New project; cshtml + test ItemGroup path update |
| CV-S6 | CardVault Api thin-root verification | PR 5 | Confirm-only; no moves expected |
| IS-S1 | IsoSwitch Domain + Application ports | PR 6 | Independent of CV slices; can start parallel to CV-S4 if branches allow |
| IS-S2 | IsoSwitch Application: handlers + ConnectorRegistry + MediatR scan | PR 7 | Depends on IS-S1 |
| IS-S3 | IsoSwitch Infrastructure extraction | PR 8 | Depends on IS-S2 (ports must exist in Application) |
| IS-S4 | IsoSwitch Api thin-root verification | PR 9 | Confirm-only; no moves expected |

---

## Task CV-S1: CardVault Domain — Platform Enums and Pure Calculators

**Spec requirements**: ARCH-1, ARCH-2, ARCH-DEP-1, ARCH-PRES-4

**Files to move/create**:
- Extract `LedgerEntryType` from `CardVault.Infrastructure.Persistence/Billing/LedgerEntryEntity.cs` → new file `CardVault.Domain/Billing/LedgerEntryType.cs`
- Extract `FeeType` from `CardVault.Infrastructure.Persistence/Billing/FeeAssessmentEntity.cs` → new file `CardVault.Domain/Billing/FeeType.cs`
- Extract `StatementStatus` from `CardVault.Infrastructure.Persistence/Billing/StatementEntity.cs` → new file `CardVault.Domain/Billing/StatementStatus.cs`
- Extract `AccountType` from `CardVault.Infrastructure.Persistence/Issuer/CardAccountEntity.cs` → new file `CardVault.Domain/Issuer/AccountType.cs`
- Extract `AccountStatus` from `CardVault.Infrastructure.Persistence/Issuer/CardAccountEntity.cs` → new file `CardVault.Domain/Issuer/AccountStatus.cs`
- Extract `CardStatus` from `CardVault.Infrastructure.Persistence/Issuer/CardEntity.cs` → new file `CardVault.Domain/Issuer/CardStatus.cs`
- Create `CardVault.Domain/Calculators/MinimumPaymentCalculator.cs` — extract pure `CalculateMinimum(StatementEntity, MinimumPaymentPolicyEntity)` logic from `CardVault.Api/Services/MinimumPaymentService.cs` as a static method; `MinimumPaymentService` retains its DB-dependent methods and calls this static
- Create `CardVault.Domain/Calculators/BillingCalculator.cs` — extract `ComputeAverageDailyBalance(decimal, List<LedgerEntryEntity>, DateTime, DateTime)` local function and `ApplyClosingTotals` formula from `CardVault.Api/Services/BillingService.cs` as public static methods
- Create `CardVault.Domain/Calculators/HoldResponseCodeCalculator.cs` — extract private static `MapResponseCode(string?)` from `CardVault.Api/Services/HoldService.cs` as a public static method
- Delete `CardVault.Domain/Class1.cs`

**Reference changes**:
- `CardVault.Domain.csproj`: add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` only if entity types pulled in require it; otherwise keep clean (no ProjectReferences — Domain references nothing)
- `CardVault.Infrastructure.Persistence.csproj`: already has `<ProjectReference Include="..\CardVault.Application\CardVault.Application.csproj" />`; ADD `<ProjectReference Include="..\CardVault.Domain\CardVault.Domain.csproj" />`
- `CardVault.Api.csproj`: already references Domain transitively via Application; verify no direct addition needed

**Namespace/using updates**:
- All 6 extracted enum files: `namespace CardVault.Domain;` (FLAT — per design.md §27 / ADR-6. Do NOT sub-namespace into `.Billing` / `.Issuer`; the design explicitly rejected that. A single `using CardVault.Domain;` is the intended rename.)
- `CardVault.Infrastructure.Persistence/Billing/LedgerEntryEntity.cs`: remove inline enum definition, add `using CardVault.Domain;`
- `CardVault.Infrastructure.Persistence/Billing/FeeAssessmentEntity.cs`: same pattern
- `CardVault.Infrastructure.Persistence/Billing/StatementEntity.cs`: same pattern
- `CardVault.Infrastructure.Persistence/Issuer/CardAccountEntity.cs`: remove both enum definitions, add `using CardVault.Domain;`
- `CardVault.Infrastructure.Persistence/Issuer/CardEntity.cs`: same pattern
- `CardVault.Api/Services/BillingService.cs`, `MinimumPaymentService.cs`, `HoldService.cs`: add `using CardVault.Domain;` where the extracted static calculators are now called (flat namespace per design ADR-6/ADR-7; no `.Calculators` sub-namespace)
- All test files referencing these enum types: global find/replace old namespace (`CardVault.Infrastructure.Persistence.Billing` / `CardVault.Infrastructure.Persistence.Issuer`) with `CardVault.Domain`

**Preserved constructs**: None impacted in this slice; `public partial class Program {}` is not touched.

**Test verification**: `dotnet test backend/CardSwitchPlatform.sln` → 650 green, 0 failed, exit 0

**Assumptions/open decisions**:
- Domain types (`StatementEntity`, `MinimumPaymentPolicyEntity`, `LedgerEntryEntity`) are EF entity types that stay in Persistence; pure calculators in Domain accept them as parameters — this is a temporary cross-layer reference tolerated per ADR-4 (full DDD deferred to Ola 4+)
- `InstallmentStatus` enum (referenced in `BillingService`) — confirm its location; extract to Domain if embedded in an entity file using the same pattern
- `BillingCalculator` and `HoldResponseCodeCalculator` have no I/O, no `DbContext`, no DI — verify before move

---

## Task CV-S2+S3: CardVault Application — Handlers + Services + Ports + MediatR Scan

**Spec requirements**: ARCH-3, ARCH-4, ARCH-5, ARCH-DEP-2, ARCH-TEST-1

**Files to move**:

Handlers (31 files, move entire directory tree):
- `CardVault.Api/Features/Catalog/Commands/CatalogCommands.cs` → `CardVault.Application/Features/Catalog/Commands/CatalogCommands.cs`
- `CardVault.Api/Features/Catalog/Queries/CatalogQueries.cs` → `CardVault.Application/Features/Catalog/Queries/CatalogQueries.cs`
- `CardVault.Api/Features/RoutingRules/Commands/RoutingRuleCommands.cs` → `CardVault.Application/Features/RoutingRules/Commands/`
- `CardVault.Api/Features/RoutingRules/Queries/RoutingRuleQueries.cs` → `CardVault.Application/Features/RoutingRules/Queries/`
- `CardVault.Api/Features/Ledger/Commands/LedgerCommands.cs` → `CardVault.Application/Features/Ledger/Commands/`
- `CardVault.Api/Features/Ledger/Queries/LedgerQueries.cs` → `CardVault.Application/Features/Ledger/Queries/`
- `CardVault.Api/Features/Risk/Commands/RiskCommands.cs` → `CardVault.Application/Features/Risk/Commands/`
- `CardVault.Api/Features/Risk/Queries/RiskQueries.cs` → `CardVault.Application/Features/Risk/Queries/`
- `CardVault.Api/Features/Settlement/Commands/SettlementCommands.cs` → `CardVault.Application/Features/Settlement/Commands/`
- `CardVault.Api/Features/Settlement/Queries/SettlementQueries.cs` → `CardVault.Application/Features/Settlement/Queries/`
- `CardVault.Api/Features/Disputes/Commands/DisputesCommands.cs` → `CardVault.Application/Features/Disputes/Commands/`
- `CardVault.Api/Features/Disputes/Queries/DisputesQueries.cs` → `CardVault.Application/Features/Disputes/Queries/`
- `CardVault.Api/Features/Tokens/Commands/TokenCommands.cs` → `CardVault.Application/Features/Tokens/Commands/`
- `CardVault.Api/Features/Tokens/Queries/TokenQueries.cs` → `CardVault.Application/Features/Tokens/Queries/`
- `CardVault.Api/Features/Billing/Commands/BillingCommands.cs` → `CardVault.Application/Features/Billing/Commands/`
- `CardVault.Api/Features/Billing/Queries/BillingQueries.cs` → `CardVault.Application/Features/Billing/Queries/`
- `CardVault.Api/Features/Ecommerce3ds/Commands/EcommerceThreeDsCommands.cs` → `CardVault.Application/Features/Ecommerce3ds/Commands/`
- `CardVault.Api/Features/Ecommerce3ds/Queries/EcommerceThreeDsQueries.cs` → `CardVault.Application/Features/Ecommerce3ds/Queries/`
- `CardVault.Api/Features/Notifications/Queries/NotificationQueries.cs` → `CardVault.Application/Features/Notifications/Queries/`
- `CardVault.Api/Features/Accounting/Commands/AccountingCommands.cs` → `CardVault.Application/Features/Accounting/Commands/`
- `CardVault.Api/Features/Accounting/Queries/AccountingQueries.cs` → `CardVault.Application/Features/Accounting/Queries/`
- `CardVault.Api/Features/Analytics/Queries/AnalyticsQueries.cs` → `CardVault.Application/Features/Analytics/Queries/`
- `CardVault.Api/Features/Issuer/Commands/IssuerCommands.cs` → `CardVault.Application/Features/Issuer/Commands/`
- `CardVault.Api/Features/Issuer/Queries/IssuerQueries.cs` → `CardVault.Application/Features/Issuer/Queries/`
- `CardVault.Api/Features/Delinquency/Commands/EvaluateDelinquencyCommand.cs` → `CardVault.Application/Features/Delinquency/Commands/`
- `CardVault.Api/Features/Delinquency/Commands/RegisterContactAttemptCommand.cs` → `CardVault.Application/Features/Delinquency/Commands/`
- `CardVault.Api/Features/Delinquency/Commands/AddDelinquencyNoteCommand.cs` → `CardVault.Application/Features/Delinquency/Commands/`
- `CardVault.Api/Features/Delinquency/Queries/GetDelinquentAccountsQuery.cs` → `CardVault.Application/Features/Delinquency/Queries/`
- `CardVault.Api/Features/Delinquency/Queries/GetContactAttemptsQuery.cs` → `CardVault.Application/Features/Delinquency/Queries/`
- `CardVault.Api/Features/Delinquency/Queries/GetDelinquencyNotesQuery.cs` → `CardVault.Application/Features/Delinquency/Queries/`
- `CardVault.Api/Features/Auth/Commands/AuthCommands.cs` → `CardVault.Application/Features/Auth/Commands/`

Services (28 business service files — excluding `AuthDecisionPublisher` which goes to CV-S4 and `Notifications/**` which goes to CV-S5):
- `AuditService.cs`, `BillingService.cs`, `BillingMaintenanceService.cs`, `DailyInterestAccrualService.cs`, `DisputeService.cs`, `DisputesService.cs`, `FeeService.cs`, `HoldService.cs`, `HoldMaintenanceService.cs`, `MinimumPaymentService.cs`, `PaymentAllocatorService.cs`, `StatementPdfService.cs`, `AvailableCreditService.cs`, `InstallmentService.cs`, `PinService.cs`, `ThreeDsService.cs`, `OpenBankingService.cs`, `AccountingService.cs`, `SettlementService.cs`, `LoyaltyService.cs`, `WalletService.cs`, `CreditPolicyService.cs`, `CreditLimitManagementService.cs`, `RiskDecisionService.cs`, `CustomerService.cs`, `LedgerService.cs`, `IssuerService.cs`, `PasswordResetService.cs`, `AnalyticsService.cs`
  All: `CardVault.Api/Services/*.cs` → `CardVault.Application/Services/*.cs`

**Ports**: NONE. `ICardVaultDbContext` was rejected (see spec ARCH-DEP-2 / design ADR-6). The EF entities live in `Infrastructure.Persistence`; moved code consumes them directly, so a `DbContext` port does not remove the `Application → Persistence` dependency. Services move wholesale and inject `CardVaultDbContext` concretely.

**Files to create (marker)**:
- `CardVault.Application/ApplicationMarker.cs` — `public sealed class ApplicationMarker {}`
- Delete `CardVault.Application/Class1.cs`

**Reference changes**:
- `CardVault.Application.csproj`: add `<ProjectReference Include="..\CardVault.Domain\CardVault.Domain.csproj" />`; add `<ProjectReference Include="..\CardVault.Infrastructure.Persistence\CardVault.Infrastructure.Persistence.csproj" />` (shared EF entity model — the documented ARCH-DEP-2 exception); add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (handlers return `IResult`/`Results.*`); add `<PackageReference Include="MediatR" Version="12.2.0" />`; add `<PackageReference Include="QuestPDF" Version="2024.3.5" />` if `StatementPdfService` needs it; verify BuildingBlocks reference already present
- `CardVault.Infrastructure.Persistence.csproj`: REMOVE the dead `<ProjectReference Include="..\CardVault.Application\...>` (unused in source; removing it makes the direction one-way `Application → Persistence` with no cycle). Keep its `Domain` + `BuildingBlocks` refs.
- `CardVault.Api.csproj`: no new references needed (already references Application and Persistence)
- `CardVault.Tests.csproj`: already references Application and Persistence — no change

**Namespace/using updates**:
- All 31 moved handler files: `namespace CardVault.Api.Features.*` → `namespace CardVault.Application.Features.*`
- All 29 moved service files: `namespace CardVault.Api.Services` → `namespace CardVault.Application.Services`
- All moved files: replace `using CardVault.Api.Services` → `using CardVault.Application.Services`; replace `using CardVault.Api.Features.*` → `using CardVault.Application.Features.*`
- All handler files referencing `IResult`/`Results.*`: these are provided by `Microsoft.AspNetCore.App` framework reference — no additional using needed if `<FrameworkReference>` is added
- ~56 CardVault test files: global find/replace `CardVault.Api.Services` → `CardVault.Application.Services` and `CardVault.Api.Features` → `CardVault.Application.Features`

**Program.cs changes** (`CardVault.Api/Program.cs`):
- Line 60: `cfg.RegisterServicesFromAssembly(typeof(Program).Assembly)` → `cfg.RegisterServicesFromAssemblyContaining<ApplicationMarker>()`
- Add `using CardVault.Application;` for `ApplicationMarker`
- All `AddScoped<ServiceName>()` registrations for moved services: change to `using CardVault.Application.Services;` (the DI type names are unchanged)
- `CardVaultDbContext` registration in `Program.cs` is unchanged — services inject the concrete `CardVaultDbContext` (no port, no extra registration).

**Preserved constructs**: `public partial class Program {}` (line ~575 of `CardVault.Api/Program.cs`) — do not remove

**Test verification**: `dotnet test backend/CardSwitchPlatform.sln` → 650 green, 0 failed, exit 0

**Assumptions/open decisions**:
- No `ICardVaultDbContext` port (rejected — ARCH-DEP-2 / ADR-6). Application references `Infrastructure.Persistence` directly for the shared EF entity model; services inject `CardVaultDbContext` concretely.
- `NotificationService.cs` in Api/Services — confirm whether it stays in Api (if it is a thin dispatcher that belongs with notification infrastructure) or moves to Application; decision: move it with Application services; its notification provider dependencies become constructor injected from Infrastructure via DI (no Application→Infrastructure reference added)
- `StatementPdfService.cs` references `QuestPDF` — confirm the package is available in Application or keep it in Api; recommendation: move it and add the package to Application.csproj
- `HoldService` and any service using `IServiceProvider` service-locator: move unchanged; the `IServiceProvider` usage is not refactored (ARCH-INV-6)

---

## [x] Task CV-S4: CardVault Infrastructure.Messaging — Kafka Adapters

**Spec requirements**: ARCH-6, ARCH-DEP-3, ARCH-PRES-4

**Files to move**:
- `CardVault.Api/Background/SwitchTxnConsumer.cs` → `CardVault.Infrastructure.Messaging/Consumers/SwitchTxnConsumer.cs`
- `CardVault.Api/Services/AuthDecisionPublisher.cs` → `CardVault.Infrastructure.Messaging/Publishers/AuthDecisionPublisher.cs`

**Files to create**:
- `CardVault.Infrastructure.Messaging/CardVault.Infrastructure.Messaging.csproj` (new project)
- Add project to `backend/CardSwitchPlatform.sln`

**Reference changes**:
- `CardVault.Infrastructure.Messaging.csproj`: `<ProjectReference>` to `CardVault.Application`, `CardVault.Infrastructure.Persistence`, `BuildingBlocks`; `<PackageReference Include="Confluent.Kafka" Version="2.6.0" />`
- `CardVault.Api.csproj`: add `<ProjectReference Include="..\CardVault.Infrastructure.Messaging\CardVault.Infrastructure.Messaging.csproj" />`

**Namespace/using updates**:
- `SwitchTxnConsumer.cs`: `namespace CardVault.Api.Background` → `namespace CardVault.Infrastructure.Messaging.Consumers`
- `AuthDecisionPublisher.cs`: `namespace CardVault.Api.Services` → `namespace CardVault.Infrastructure.Messaging.Publishers`
- `CardVault.Api/Program.cs`: update `using CardVault.Api.Background` → `using CardVault.Infrastructure.Messaging.Consumers`; update `using CardVault.Api.Services` for `AuthDecisionPublisher` → `using CardVault.Infrastructure.Messaging.Publishers`

**Program.cs changes**: DI registrations for `SwitchTxnConsumer` and `AuthDecisionPublisher` remain in `Program.cs`; only the `using` directives change

**Preserved constructs**: `IServiceProvider` usage inside `SwitchTxnConsumer` moves unchanged (ARCH-INV-6)

**Test verification**: `dotnet test backend/CardSwitchPlatform.sln` → 650 green, 0 failed, exit 0

**Assumptions/open decisions**:
- Verify `SwitchTxnConsumer` uses types from `CardVault.Api.Contracts` — if so, `CardVault.Infrastructure.Messaging` must reference the Contracts or those types must stay in Api and be referenced via the Api csproj (acceptable since Api → Messaging is wrong direction); if Contracts are API-layer types, `SwitchTxnConsumer` must be updated to use Application equivalents or the Contracts file must move to Application

---

## [x] Task CV-S5: CardVault Infrastructure.Notifications — Notification Adapters

**Spec requirements**: ARCH-7, ARCH-PRES-4

**Files to move** (24 source files + 10 cshtml templates):
- All of `CardVault.Api/Services/Notifications/**/*.cs` (24 files):
  `NotificationDispatcher.cs`, `NotificationDispatcherOptions.cs`, `NotificationProviderRegistry.cs`, `DeliveryStateMachine.cs`, `InvalidDeliveryTransitionException.cs`, `INotificationProvider.cs`, `Providers/SendGridEmailProvider.cs`, `Providers/SendGridOptions.cs`, `Providers/TwilioSmsProvider.cs`, `Providers/TwilioOptions.cs`, `Providers/MovistarEcuadorSmsProvider.cs`, `Providers/MovistarOptions.cs`, `Webhooks/SendGridWebhookSignatureValidator.cs`, `Webhooks/SendGridWebhookOptions.cs`, `Webhooks/TwilioWebhookSignatureValidator.cs`, `Webhooks/TwilioWebhookOptions.cs`, `Webhooks/MovistarWebhookSignatureValidator.cs`, `Webhooks/MovistarWebhookOptions.cs`, `Webhooks/WebhookValidatorHelper.cs`, `Templates/INotificationTemplateRenderer.cs`, `Templates/RazorNotificationTemplateRenderer.cs`, `Templates/PciTemplateGuard.cs`, `Templates/PciTemplateViolationException.cs`, `Templates/TemplateModel.cs`
  → `CardVault.Infrastructure.Notifications/` (mirror directory structure)
- `CardVault.Api/Services/NotificationService.cs` — if moved in CV-S2+S3, verify it moved; if it references notification provider types, its `using` directives need updating
- All 10 `.cshtml` template files: `CardVault.Api/Services/Notifications/Templates/*.cshtml` → `CardVault.Infrastructure.Notifications/Templates/`

**Files to create**:
- `CardVault.Infrastructure.Notifications/CardVault.Infrastructure.Notifications.csproj` (new project)
- Add project to `backend/CardSwitchPlatform.sln`

**Reference changes**:
- `CardVault.Infrastructure.Notifications.csproj`: `<ProjectReference>` to `CardVault.Application`, `BuildingBlocks`; `<PackageReference Include="RazorLight" Version="2.3.1" />`; add the `CopyToOutputDirectory` ItemGroup for templates:
  ```xml
  <ItemGroup>
    <RazorCompile Remove="Templates\*.cshtml" />
    <Content Update="Templates\*.cshtml">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  ```
- `CardVault.Api.csproj`: remove `<PackageReference Include="RazorLight" …/>` (unless still needed); add `<ProjectReference>` to `CardVault.Infrastructure.Notifications`; remove the `Services\Notifications\Templates\*.cshtml` ItemGroup (now in Notifications project)
- `CardVault.Tests.csproj`: update the `Content Include` path from `..\..\src\CardVault.Api\Services\Notifications\Templates\*.cshtml` → `..\..\src\CardVault.Infrastructure.Notifications\Templates\*.cshtml`

**Namespace/using updates**:
- All 24 moved CS files: `namespace CardVault.Api.Services.Notifications*` → `namespace CardVault.Infrastructure.Notifications.*`
- `CardVault.Api/Program.cs`: update all `using CardVault.Api.Services.Notifications*` → `using CardVault.Infrastructure.Notifications.*`

**Program.cs changes**: DI registrations for notification providers/registry/dispatcher stay in `Program.cs`; only `using` directives change

**Test verification**: `dotnet test backend/CardSwitchPlatform.sln` → 650 green, 0 failed, exit 0; specifically verify `RazorNotificationTemplateRendererTests` finds templates at the new output path

**Assumptions/open decisions**:
- `RazorNotificationTemplateRenderer.Create()` uses `AppContext.BaseDirectory` to find templates; the path it constructs must match the new output directory structure — verify the path constant inside the renderer after the move

---

## [x] Task CV-S6: CardVault Api — Thin Composition Root Verification

**Spec requirements**: ARCH-8, ARCH-PRES-1, ARCH-PRES-4

**Files to move**: None — this is a verification-only slice

**Checklist**:
- [x] Confirm `CardVault.Api` source tree contains NO handler, business service, Kafka adapter, or notification provider — verified via rg, NONE FOUND
- [x] Confirm remaining files: `Program.cs`, `Controllers/**`, `Contracts/**`, `Background/**` (HoldExpiryWorker, DelinquencyEvaluationWorker, NotificationDispatcherWorker), `Pci/**`, `Vault/**`, `Security/**`, `Observability.cs`
- [x] Confirm `public partial class Program {}` declaration is present (Program.cs:585)
- [x] `dotnet build backend/CardSwitchPlatform.sln` — 0 errors, no circular references (13 preexisting warnings)

**Verification notes**:
- `Background/NotificationDispatcherWorker.cs` also remains in Api — it is a `BackgroundService` host concern with no business logic (resolves `INotificationDispatcher` from DI and calls `DispatchBatchAsync`); the dispatcher implementation lives in `CardVault.Infrastructure.Notifications`. Same category as the other two workers; legitimately stays in Api.
- Removed empty leftover `CardVault.Api/Services/` directory (ghost dir left after the CV-S2..S5 moves; untracked by git).

**Test verification**: `dotnet test backend/CardSwitchPlatform.sln` → 650 green, 0 failed, exit 0

**Assumptions/open decisions**:
- `HoldExpiryWorker.cs` and `DelinquencyEvaluationWorker.cs` are host-lifecycle workers (background services) — they belong in Api as host concerns; verify they do not contain business logic that should have moved
- `Security/TokenService.cs`, `Security/PermissionCatalog.cs` — host-specific auth concerns; stay in Api

---

## [x] Task IS-S1: IsoSwitch Domain + Application Ports

**Deviations from original plan (verified against live code):**
- `IRoutingEngineV2` is NOT in `IsoSwitch.Api` (the task assumed so) — it lives in `Infrastructure.SwitchIso8583/Routing/`. It was **NOT moved** to Application.Ports: its return type `RoutingDecision` embeds EF entities (`BinRangesCache`, `RoutingRulesV2` from `Infrastructure.Persistence`), a data-type dependency a port cannot break (same rationale as CardVault ADR-6 rejecting `ICardVaultDbContext`). Deferred to IS-S2/S3 under the ARCH-DEP-2 compromise. Stays in Infrastructure for now.
- `IsoMessage` (POCO, dependency-free) was MOVED `Infrastructure.SwitchIso8583/Iso/` → `IsoSwitch.Domain/` (user-approved). Required because `IIsoAuditService.LogAsync(..., IsoMessage, ...)` references it; with IsoMessage in Domain the port moves to Application.Ports cleanly without Application referencing Infrastructure.
- `OriginalDataElementsBuilder.BuildFromConfig(IConfiguration, ...)` wrapper REMOVED (it pulled `IConfiguration` into Domain). The pure `Build(...primitives...)` method moves to Domain; the single caller `Field90Service` (Api) now extracts `Iso:AcqInstId`/`Iso:FwdInstId` from config and calls `Build` (ADR-7-style primitive re-parameterization).
- Moved Domain/Port types resolved via project-level `<Using Include="..." />` global usings (Api, Infrastructure.SwitchIso8583, Tests) instead of per-file using edits — keeps the ISO codec files untouched.

**Build: 0 errors, no circular refs. Tests: 650 green. Domain is dependency-free (0 ProjectReferences).**

---

### Original task spec (for reference)

**Spec requirements**: ARCH-9, ARCH-10, ARCH-DEP-1, ARCH-DEP-2

**Files to move/create**:
- `IsoSwitch.Api/TransactionStateMachine.cs` → `IsoSwitch.Domain/TransactionStateMachine.cs`
- `IsoSwitch.Api/TransactionTypes.cs` → `IsoSwitch.Domain/TransactionTypes.cs` (contains `TransactionTypes` + `TransactionStatuses`)
- `IsoSwitch.Api/OriginalDataElementsBuilder.cs` → `IsoSwitch.Domain/OriginalDataElementsBuilder.cs`
- Delete `IsoSwitch.Domain/Class1.cs`
- `IsoSwitch.Api/IIsoAuditService.cs` → `IsoSwitch.Application/Ports/IIsoAuditService.cs`
- `IsoSwitch.Api/ISwitchEventPublisher.cs` → `IsoSwitch.Application/Ports/ISwitchEventPublisher.cs`
- `IsoSwitch.Api/IRoutingEngineV2.cs` (confirm file location) → `IsoSwitch.Application/Ports/IRoutingEngineV2.cs`
- Create `IsoSwitch.Application/ApplicationMarker.cs` — `public sealed class ApplicationMarker {}`
- Delete `IsoSwitch.Application/Class1.cs`

**Reference changes**:
- `IsoSwitch.Domain.csproj`: no ProjectReferences (pure domain); verify no outward references
- `IsoSwitch.Application.csproj`: add `<ProjectReference Include="..\IsoSwitch.Domain\IsoSwitch.Domain.csproj" />`; add `<PackageReference Include="MediatR" Version="12.2.0" />`; confirm BuildingBlocks reference if needed
- `IsoSwitch.Api.csproj`: add `<ProjectReference Include="..\IsoSwitch.Application\IsoSwitch.Application.csproj" />` if not already present; add `<ProjectReference Include="..\IsoSwitch.Domain\IsoSwitch.Domain.csproj" />`

**Namespace/using updates**:
- 3 moved Domain files: `namespace IsoSwitch.Api` → `namespace IsoSwitch.Domain`
- 3 moved port interface files: `namespace IsoSwitch.Api` → `namespace IsoSwitch.Application.Ports`
- `IsoSwitch.Api/Program.cs`: add `using IsoSwitch.Domain;`, `using IsoSwitch.Application.Ports;` where types are referenced
- All IsoSwitch test files referencing these types: update namespace usings

**Preserved constructs**:
- `public partial class Program {}` (line ~328 of `IsoSwitch.Api/Program.cs`) — do not remove
- `BinRoutingStore.InitializeFromDbAsync` and `PanMapStore.InitializeFromDbAsync` startup calls (lines ~190–191) — untouched in this slice

**Test verification**: `dotnet test backend/CardSwitchPlatform.sln` → 650 green, 0 failed, exit 0

**Assumptions/open decisions**:
- `IRoutingEngineV2` — confirm it is defined in `IsoSwitch.Api` (only `IIsoAuditService` and `ISwitchEventPublisher` are confirmed via file listing; `IRoutingEngineV2` is used at line 143 of `Program.cs` but the interface file is not in the listing); locate its file before the slice
- `TransactionStateMachine` — check whether it has any infrastructure imports; if it references EF or Kafka types it cannot move to Domain as-is

---

## Task IS-S2: IsoSwitch Application — Handlers + ConnectorRegistry + MediatR Scan

**Spec requirements**: ARCH-11, ARCH-DEP-2, ARCH-PRES-4

**Files to move**:
- `IsoSwitch.Api/Features/Transactions/Commands/AuthorizeTransaction/AuthorizeTransactionCommand.cs` → `IsoSwitch.Application/Features/Transactions/Commands/AuthorizeTransaction/`
- `IsoSwitch.Api/Features/Transactions/Commands/AuthorizeTransaction/AuthorizeTransactionCommandHandler.cs` → `IsoSwitch.Application/Features/Transactions/Commands/AuthorizeTransaction/`
- `IsoSwitch.Api/Features/Transactions/Commands/CaptureTransaction/CaptureTransactionCommand.cs` → `IsoSwitch.Application/Features/Transactions/Commands/CaptureTransaction/`
- `IsoSwitch.Api/Features/Transactions/Commands/CaptureTransaction/CaptureTransactionCommandHandler.cs` → `IsoSwitch.Application/Features/Transactions/Commands/CaptureTransaction/`
- `IsoSwitch.Api/Features/Transactions/Commands/ReversalTransaction/ReversalTransactionCommand.cs` → `IsoSwitch.Application/Features/Transactions/Commands/ReversalTransaction/`
- `IsoSwitch.Api/Features/Transactions/Commands/ReversalTransaction/ReversalTransactionCommandHandler.cs` → `IsoSwitch.Application/Features/Transactions/Commands/ReversalTransaction/`
- `IsoSwitch.Api/Features/Transactions/Commands/ReversalAdvice/ReversalAdviceCommand.cs` → `IsoSwitch.Application/Features/Transactions/Commands/ReversalAdvice/`
- `IsoSwitch.Api/Features/Transactions/Commands/ReversalAdvice/ReversalAdviceCommandHandler.cs` → `IsoSwitch.Application/Features/Transactions/Commands/ReversalAdvice/`
- `IsoSwitch.Api/Features/Transactions/Commands/NetworkManagement/NetworkCommand.cs` → `IsoSwitch.Application/Features/Transactions/Commands/NetworkManagement/`
- `IsoSwitch.Api/Features/Transactions/Commands/NetworkManagement/NetworkCommandHandler.cs` → `IsoSwitch.Application/Features/Transactions/Commands/NetworkManagement/`
- `IsoSwitch.Api/IsoConnectorConfig.cs` → `IsoSwitch.Application/Config/IsoConnectorConfig.cs`
- `IsoSwitch.Api/IsoToolsDtos.cs` → `IsoSwitch.Application/Dtos/IsoToolsDtos.cs`
- `IsoSwitch.Api/RoutingV24Dtos.cs` → `IsoSwitch.Application/Dtos/RoutingV24Dtos.cs`
- Integration-event record types currently at the bottom of `IsoSwitch.Api/Program.cs` (e.g., `SimPurchaseRequest`, `SimAuthRequest`, `PanMapRequest`, etc.): extract to `IsoSwitch.Application/Dtos/SimulatorDtos.cs`; note `DbMigrateWorker` stays embedded or moves to Infrastructure (handled in IS-S3)
- `IsoSwitch.Api/ConnectorRegistry.cs` (if exists — confirm; otherwise it may be inline in Program.cs or in Infrastructure.SwitchIso8583) → `IsoSwitch.Application/Config/ConnectorRegistry.cs`

**Namespace/using updates**:
- All 10 handler files: `namespace IsoSwitch.Api.Features.*` → `namespace IsoSwitch.Application.Features.*`
- `IsoConnectorConfig.cs`: `namespace IsoSwitch.Api` → `namespace IsoSwitch.Application.Config`
- All DTO files: `namespace IsoSwitch.Api` → `namespace IsoSwitch.Application.Dtos`
- `IsoSwitch.Api/Program.cs`: `using IsoSwitch.Application.Features.*`, `using IsoSwitch.Application.Config;`, `using IsoSwitch.Application.Dtos;`

**Program.cs changes** (`IsoSwitch.Api/Program.cs`):
- Line 92: `cfg.RegisterServicesFromAssembly(typeof(Program).Assembly)` → `cfg.RegisterServicesFromAssemblyContaining<ApplicationMarker>()`
- Add `using IsoSwitch.Application;`
- `ConnectorRegistry` DI registration (line ~147): namespace update

**Preserved constructs**:
- `public partial class Program {}` (line ~328)
- `BinRoutingStore.InitializeFromDbAsync` + `PanMapStore.InitializeFromDbAsync` startup calls (lines ~190–191)
- `DbMigrateWorker` class remains in `Program.cs` for now (removed in IS-S3)

**Test verification**: `dotnet test backend/CardSwitchPlatform.sln` → 650 green, 0 failed, exit 0; boot tests that dispatch MediatR commands MUST pass

**Assumptions/open decisions**:
- `ConnectorRegistry` — confirm whether it lives in `IsoSwitch.Api` or `IsoSwitch.Infrastructure.SwitchIso8583`; if in Infrastructure, it cannot move to Application without a port; investigate before this slice
- Simulator DTOs at the bottom of `Program.cs` (`SimPurchaseRequest`, etc.) are dev/demo types — confirm they are not referenced by the production codec before moving

---

## Task IS-S3: IsoSwitch Infrastructure Extraction

**Spec requirements**: ARCH-12, ARCH-DEP-3

**Files to move**:
- `IsoSwitch.Api/Consumers/ConfigSyncConsumer.cs` → `IsoSwitch.Infrastructure.Consumers/ConfigSyncConsumer.cs` (project already exists with `Class1.cs`)
- Delete `IsoSwitch.Infrastructure.Consumers/Class1.cs`
- `DbMigrateWorker` class (currently embedded in `IsoSwitch.Api/Program.cs` or extracted inline) → `IsoSwitch.Infrastructure.Persistence/DbMigrateWorker.cs`
- `IsoSwitch.Api/IsoAuditService.cs` (concrete `IIsoAuditService` impl) → `IsoSwitch.Infrastructure.Persistence/IsoAuditService.cs`
- `IsoSwitch.Api/BinaryIsoAuditService.cs` → `IsoSwitch.Infrastructure.Persistence/BinaryIsoAuditService.cs`
- `IsoSwitch.Api/SwitchEventPublisher.cs` (concrete `ISwitchEventPublisher` impl) → appropriate Infra project (confirm whether SwitchIso8583 or Persistence)
- `IsoSwitch.Api/CatalogAuditPersistence.cs` → `IsoSwitch.Infrastructure.Persistence/CatalogAuditPersistence.cs`

**Reference changes**:
- `IsoSwitch.Infrastructure.Consumers.csproj`: add `<ProjectReference>` to `IsoSwitch.Application` (to see port interfaces), `IsoSwitch.Infrastructure.Persistence`, `BuildingBlocks`; add Kafka packages
- `IsoSwitch.Infrastructure.Persistence.csproj`: add `<ProjectReference>` to `IsoSwitch.Application` (for `IIsoAuditService` port) if not already present
- `IsoSwitch.Api.csproj`: add `<ProjectReference>` to `IsoSwitch.Infrastructure.Consumers`

**Namespace/using updates**:
- `ConfigSyncConsumer.cs`: `namespace IsoSwitch.Api.Consumers` → `namespace IsoSwitch.Infrastructure.Consumers`
- `IsoAuditService.cs`, `BinaryIsoAuditService.cs`, `CatalogAuditPersistence.cs`: `namespace IsoSwitch.Api` → `namespace IsoSwitch.Infrastructure.Persistence`
- `IsoSwitch.Api/Program.cs`: update `using` for all moved types

**Preserved constructs**:
- `BinRoutingStore.InitializeFromDbAsync` + `PanMapStore.InitializeFromDbAsync` startup calls — preserved verbatim
- `public partial class Program {}` — preserved

**Test verification**: `dotnet test backend/CardSwitchPlatform.sln` → 650 green, 0 failed, exit 0

**Assumptions/open decisions**:
- `SwitchEventPublisher` — confirm which Infra project is its home (it publishes Kafka events; likely `IsoSwitch.Infrastructure.SwitchIso8583` or a new `IsoSwitch.Infrastructure.Messaging`); do NOT create a new project if an existing one is the correct home
- `ReversalWorker.cs` in `IsoSwitch.Api` (listed in file glob) — determine if it is a host-lifecycle concern (stays in Api) or an infrastructure adapter (moves here)

---

## Task IS-S4: IsoSwitch Api — Thin Composition Root Verification

**Spec requirements**: ARCH-13, ARCH-PRES-1, ARCH-PRES-2, ARCH-PRES-3

**Files to move**: None — verification-only slice

**Checklist**:
- [ ] Confirm `IsoSwitch.Api` contains ONLY: `Program.cs`, `Endpoints/**`, `Tcp/**` (TCP dev codec + `TcpIso8583Server`), `Iso8583/**` (primary ISO codec — confirm it stays separate from dev codec), `Observability.cs`, `Security/**`, `Routing/BinRoutingStore.cs`, `Persistence/JsonFileStore.cs`
- [ ] Confirm NO handler, `ConnectorRegistry`, consumer, `IsoAuditService`/`SwitchEventPublisher` implementation remains in `IsoSwitch.Api` source
- [ ] Confirm `TcpIso8583Server` and its dev codec types remain in `IsoSwitch.Api/Tcp/`
- [ ] Confirm `BinRoutingStore.InitializeFromDbAsync` + `PanMapStore.InitializeFromDbAsync` appear in startup block in original order
- [ ] Confirm `public partial class Program {}` declaration is present (line ~328)
- [ ] `dotnet build backend/CardSwitchPlatform.sln` — no errors

**Test verification**: `dotnet test backend/CardSwitchPlatform.sln` → 650 green, 0 failed, exit 0

**Assumptions/open decisions**:
- `IsoSimulatorServer.cs` in `IsoSwitch.Api` — confirm it is a dev-mode host concern; if so it stays in Api; if it is an infrastructure adapter it should have moved in IS-S3

---

## Overall Assumptions and Constraints

- **No DbContext port**: `ICardVaultDbContext` was rejected (ARCH-DEP-2 / ADR-6) — it abstracts the context but not the entity types that force the `Application → Persistence` reference. Application references `Infrastructure.Persistence` directly; the dead reverse reference is removed so the direction is one-way.
- **ApplicationMarker placement**: One dedicated `public sealed class ApplicationMarker {}` per Application assembly (CardVault and IsoSwitch); not reusing an existing public type.
- **Test instantiation pattern preservation**: Tests that `new` concrete services (e.g., `new BillingService(db, ...)`) continue to compile because those types still exist — only their namespace changes. No test is rewritten to use interfaces or mocks (ARCH-INV-4).
- **No logic changes**: Method bodies in moved types are byte-for-byte identical to the pre-change baseline. Only `namespace` declarations and `using` directives change (ARCH-PRES-4).
- **650 tests green gate per slice**: Every slice commit must pass `dotnet test backend/CardSwitchPlatform.sln` before the next slice starts. A failing gate = revert the slice, fix, re-test.
- **EF entities stay in Persistence**: `CardVault.Infrastructure.Persistence` retains all entity types and `CardVaultDbContext`. No entity moves to Domain (ARCH-INV-1). Domain types (enums, pure calculators) accept entity types as parameters — this is a pragmatic bridge tolerated until Ola 4+.
- **IsoAudit untouched**: `IsoAudit.Api` is out of scope; no files in that service are modified (ARCH-INV-2).
- **IsoSwitch static stores**: Only 2 `InitializeFromDbAsync` calls exist in the live code (`BinRoutingStore` at line 190, `PanMapStore` at line 191); the spec mentions a 3rd (`IsoTraceStore`) that is not present — preserve what exists, do not invent.
- **Codec separation**: `IsoSwitch.Api/Tcp/TcpIso8583Server.cs` and associated dev codec types stay in Api; `IsoSwitch.Api/Iso8583/` (primary ISO codec) stays in its current home; these are never merged (ARCH-PRES-3).
- **New projects** (`CardVault.Infrastructure.Messaging`, `CardVault.Infrastructure.Notifications`): must be added to `backend/CardSwitchPlatform.sln` before build; use `dotnet sln add` or edit the `.sln` file directly.
