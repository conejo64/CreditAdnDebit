# Design: Commercial Product Readiness — Demo Containment

## Technical Approach

Add a shared fail-closed commercial governance layer for CardVault, IsoSwitch, IsoAudit, and Angular. This slice is demo containment and truthful disclosure only: it blocks simulator-backed approvals and side effects in commercial mode. It does not solve the separate PAN/PIN boundary change; instead it rejects or removes sensitive simulator-backed inputs until that later change provides a PCI-safe path.

## Architecture Decisions

| Decision | Choice | Alternatives / Rationale |
|---|---|---|
| Mode authority | Create `CommercialOptions` as a parameterless mutable class in `backend/shared/BuildingBlocks/Commercial`, with initialized defaults `Mode=Commercial`, all exposure flags `false`, and `AddOptions<CommercialOptions>().BindConfiguration(...).ValidateOnStart()` plus `IValidateOptions<CommercialOptions>`. | Positional records do not match existing bindable options patterns. `IHostEnvironment` alone is too broad and unsafe. |
| IsoSwitch fail-closed path | Commercial mode must not register `SimulatorConnector`, must make `Iso:ForceSimulator` invalid, and must remove `SIMULATOR` fallback from `ConnectorRegistry`, `RoutingEngine`, and `RoutingEngineV2`; unresolved commercial routing fails before connector/audit/event mutation. | Gating only `MapSimulatorEndpoints()` leaves `/api/iso/*` able to accept PAN/Track2/PIN and return `Confirmed/APPROVED` through simulator fallback. |
| Sensitive operational inputs | In commercial mode, `/api/iso/authorize`, `/api/iso/capture`, reversal, reversal-advice, and network commands must either be unmapped through a commercial endpoint gate or reject simulator-backed PAN, Track2, PIN block, EMV, and raw ISO fields before MediatR handlers run. | UI-only hiding or demo-route-only blocking is not fail-closed. No approved simulation result or transaction side effect may occur. |
| Claim disclosure | Keep the governed evidence register server-side/internal only. Expose a sanitized `/api/commercial/claims` DTO containing only permitted capability id, maturity, allowed mode, and commercial message; no owner, reviewer, evidence URI/hash, expiry, or internal notes. Require an authorization policy for service endpoints. | Mirroring the internal register into Angular `assets` publishes governance metadata and weakens review control. |
| Audit | Record startup mode, denied simulator registration, routing validation, and sanitized public register version as PCI-safe local audit events. | Avoid synchronous cross-context governance calls; each bounded context owns evidence. |

## Data Flow

```text
config/env -> CommercialOptions validation -> service startup
  |-> docs/diagnostics route gate
  |-> IsoSwitch connector registration: commercial excludes SIMULATOR
  |-> routing: no SIMULATOR fallback; unresolved route fails closed
  |-> `/api/iso/*` commercial guard: reject sensitive simulator-backed inputs before side effects
internal claim register -> policy-protected disclosure API -> Angular disclosure service
```

## File Changes

| File | Action | Description |
|---|---|---|
| `backend/shared/BuildingBlocks/Commercial/*` | Create | `CommercialMode`, mutable `CommercialOptions`, validator, exposure predicates, internal register contracts, sanitized disclosure DTOs. |
| `backend/services/IsoSwitch/src/IsoSwitch.Api/Program.cs` | Modify | Register/validate commercial options; conditionally register `SimulatorConnector`; fail startup if commercial config enables simulator fallback; gate Swagger/metrics/demo docs. |
| `backend/services/IsoSwitch/src/IsoSwitch.Application/Config/ConnectorRegistry.cs` | Modify | Remove unconditional simulator fallback; reject `Iso:ForceSimulator` outside demo. |
| `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/Routing/*.cs` | Modify | Return explicit no-route failures instead of `SIMULATOR` fallback in commercial mode. |
| `backend/services/IsoSwitch/src/IsoSwitch.Api/Endpoints/TransactionEndpoints.cs` | Modify | Add commercial guard for `/api/iso/*` sensitive payloads and no-side-effect rejection before handlers. |
| `backend/services/IsoSwitch/src/IsoSwitch.Api/Endpoints/SimulatorEndpoints.cs` | Modify | Map only in explicit demo mode; no audit/event mutation when unavailable. |
| `backend/services/CardVault/src/CardVault.Api/Program.cs`, `backend/services/IsoAudit/src/IsoAudit.Api/Program.cs` | Modify | Reuse docs/diagnostic gate and expose only policy-protected sanitized commercial disclosure where needed. |
| `backend/services/*/appsettings*.json` | Modify | Add fail-closed `Commercial` defaults; development demo config opts in explicitly. |
| `frontend/src/app/core/commercial-governance.service.ts`, route/sidebar/simulator/card UI | Modify/Create | Load disclosure DTO from API, hide simulator, show simulated/unavailable banners, and block unavailable issue/PIN actions without fake success. |

## Interfaces / Contracts

```csharp
public sealed class CommercialOptions
{
    public const string Section = "Commercial";
    public CommercialMode Mode { get; set; } = CommercialMode.Commercial;
    public bool EnableDemoSurfaces { get; set; }
    public bool EnableAnonymousDiagnostics { get; set; }
    public bool EnableSwagger { get; set; }
    public string ClaimRegisterVersion { get; set; } = "unpublished";
}
```

Internal register includes owner/evidence/reviewer metadata. Public DTO exposes only `capabilityId`, `label`, `maturity`, `permittedModes`, and `commercialMessage`.

## Testing Strategy

| Layer | What | Approach |
|---|---|---|
| Unit | options validator, no simulator fallback, disclosure sanitizer | xUnit/FluentAssertions with fail-closed defaults. |
| Integration | commercial `/api/iso/*` cannot produce simulator `APPROVED`; simulator/docs absent; no audit/Kafka side effects on denied calls | `WebApplicationFactory<Program>`. |
| Frontend | API-backed disclosure, hidden simulator, unavailable actions | Angular specs. |

## Migration / Rollout

No data migration required. Commercial deployments deny simulator, docs, diagnostics, and unsupported actions by default. Rollback can re-enable demos only in isolated non-commercial environments.

## Open Questions

None for demo containment. HSM, rail certification, browser PAN, and clear-PIN remediation remain follow-on gates.
