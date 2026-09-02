# Tasks: CardTech Commercial Product Readiness

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 650-950 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 foundation/config; PR 2 IsoSwitch fail-closed; PR 3 claim API + UI/docs/tests |
| Delivery strategy | ask-on-risk |
| Chain strategy | feature-branch-chain |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | Commercial options + shared contracts | PR 1 | `backend/shared/BuildingBlocks/Commercial/*`; foundation for all services |
| 2 | Fail-closed switch/runtime guard | PR 2 | `IsoSwitch` startup, routing, simulator, audit denial path |
| 3 | Disclosure API + Angular governance UI | PR 3 | sanitized claims, service wiring, docs, tests |

## Phase 1: Foundation
- [x] 1.1 Add `backend/shared/BuildingBlocks/Commercial/CommercialOptions.cs`, `CommercialMode.cs`, and `CommercialOptionsValidator.cs` with fail-closed defaults and bind/validate-on-start wiring.
- [x] 1.2 Add internal claim-register and public DTO contracts in `backend/shared/BuildingBlocks/Commercial/ClaimRegister*.cs` and `CommercialDisclosureDto.cs`; keep evidence metadata internal only.
- [x] 1.3 Add commercial config defaults to `backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.json`, `appsettings.Development.json`, and matching service configs for CardVault/IsoAudit.

## Phase 2: IsoSwitch Fail-Closed Runtime
- [x] 2.1 Update `backend/services/IsoSwitch/src/IsoSwitch.Api/Program.cs` to register/validate `CommercialOptions`, gate Swagger/demo surfaces, and skip `SimulatorConnector` in commercial mode.
- [x] 2.2 Remove simulator fallback in `backend/services/IsoSwitch/src/IsoSwitch.Application/Config/ConnectorRegistry.cs` and `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/Routing/*.cs` so unresolved commercial routing fails before mutation.
- [x] 2.3 Gate `backend/services/IsoSwitch/src/IsoSwitch.Api/Endpoints/TransactionEndpoints.cs` and `SimulatorEndpoints.cs` so commercial `/api/iso/*` rejects simulator-backed PAN/Track2/PIN/EMV fields before handlers.
- [x] 2.4 Add startup/audit records in `backend/services/IsoSwitch/src/IsoSwitch.Api/Background/*` or `Observability.cs` for denied simulator registration and commercial-mode startup.

### Phase 2 verification
`dotnet test backend/services/IsoSwitch/tests/IsoSwitch.Tests/IsoSwitch.Tests.csproj` — **99/99 pass**.
Each gate is covered in both directions, so demo mode is proven to keep the behaviour commercial mode
refuses: `CommercialMode_WithoutRoutingRule_FailsBeforeSimulatorFallback` against
`DemoMode_WithoutRoutingRule_PreservesSimulatorFallback`,
`CommercialMode_UnregisteredConnector_DoesNotFallbackToSimulator` against
`DemoMode_UnregisteredConnector_PreservesSimulatorFallback`, and
`CommercialMode_AuthorizeWithSensitiveFields_IsRejectedBeforeHandler` against
`DemoMode_AuthorizeWithSyntheticFields_AllowsHandler`.

2.4 was implemented inline in `Program.cs` and could not be covered without booting the API, so the
record-composition rules moved to `Background/CommercialStartupAudit.cs` — the location this task
already called for. `Program.cs` now writes whatever that returns.

## Phase 3: Disclosure, UI, and Docs
- [ ] 3.1 Add policy-protected sanitized disclosure endpoints in `backend/services/CardVault/src/CardVault.Api/Program.cs` and `backend/services/IsoAudit/src/IsoAudit.Api/Program.cs`.
- [ ] 3.2 Add Angular governance service/UI in `frontend/src/app/core/*`, `frontend/src/app/features/switch/simulator.component.ts`, `routing.component.ts`, `sidebar.component.ts`, and related templates to hide unavailable actions and show simulated/unavailable states.
- [ ] 3.3 Update runbook/docs in `openspec/changes/commercial-product-readiness/proposal.md`, `design.md`, and a new commercial-mode runbook under `backend/deploy/` or `docs/`.
- [ ] 3.4 Write tests: backend unit/integration under `backend/services/IsoSwitch/tests/*`, `backend/services/CardVault/tests/*`, `backend/services/IsoAudit/tests/*`; frontend specs under `frontend/src/app/**/**/*.spec.ts` for fail-closed mode, sanitized claims, and disabled simulator flows.

