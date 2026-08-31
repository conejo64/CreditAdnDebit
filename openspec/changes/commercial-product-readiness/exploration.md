## Exploration: commercial-product-readiness

### Baseline and Scope

This exploration is re-baselined against Git `main` at `15a98a06e87e43641255a9325088e168c2f30e26` (2026-08-28), using `git show main:<path>`; it does not treat the checked-out `feature/observability-grafana` branch as authoritative. The program covers commercial readiness for a **dedicated, non-money-moving client pilot** first, followed by a separate production-payment readiness decision. It does not authorize a live switch, PCI DSS claim, network certification claim, or handling of live PAN/PIN.

### Current State and Re-baselined Findings

| Group | Classification on `main` | Evidence and required outcome |
|---|---|---|
| Card data / PIN boundary (P0) | **Confirmed still on main** | `frontend/src/app/features/issuer/cards/card-list.component.ts` creates a PAN with `Math.random()` and posts it; it also posts a clear PIN. `CardVault.Application/Services/IssuerService.cs` stores the issuance PAN as Base64 plaintext while labeling it dev-only. Remove or hard-disable these flows for commercial deployments and design a vault/HSM-owned issuance and PIN-block boundary. |
| HSM maturity (P0) | **Confirmed still on main** | `HardwareHsmProvider.SendCommand` always throws `SocketException`; `TryParsePinBlock` exposes `out string clearPin`; `SoftHsmProvider` returns `"1234"`. The `IHsmService` abstraction and tests were merged, but no operational HSM integration is demonstrated. |
| Rail maturity (P0) | **Confirmed still on main** | Banred/Datafast connectors and packagers are in `main`, but both packagers are explicitly skeletons delegating to `SimpleIso8583Packager`; `Iso:ForceSimulator` is true in `IsoSwitch.Api/appsettings.json`. Quarantine simulator paths and obtain one rail's approved field mapping, network agreement, and certification evidence before treating it as a rail. |
| Demo / attack-surface containment (P0) | **Confirmed still on main** | `SimulatorEndpoints.cs` exposes multiple `AllowAnonymous` `/api/demo/*` and `/api/simulator/options` helpers, including raw-PAN diagnostics; `Program.cs` maps Swagger unconditionally. Establish an explicit non-production profile and prove demo/diagnostic routes are absent or blocked in commercial environments. |
| Sensitive audit-data controls (P0) | **Confirmed gap; full extent unverified** | `IsoSwitch.Infrastructure.Persistence/AuditService.cs` serializes arbitrary payloads without a central redaction policy. CardVault has masking and selected tests, but every log, trace, event, response, and audit sink needs an inventory and automated negative tests. |
| Secrets and configuration (P0) | **Partially implemented; operational state unknown** | `main` has secret-shape validators, `.env.example`, a gitleaks CI job, and an active `sec9-config-secret-remediation` OpenSpec change. Rotation of any historical exposure, deployed secrets-manager use, and environment fail-fast evidence remain unverified. |
| Authorization / approval controls (P1) | **Partially implemented; maker-checker unresolved** | Contrary to the previous audit, `main` policy definitions give `Auditor` view—not manage—access for billing, disputes, settlement, accounting, and collections. Do not carry that old claim forward. Action-level authorization still requires tests, and no durable maker-checker/dual-control workflow was identified. |
| Event delivery and idempotency (P1) | **Partially implemented; resilience unproven** | `KafkaConsumerWorker` disables auto commit and publishes retry/DLQ; ISO transaction commands persist idempotency keys. Failure atomicity, consumer-wide duplicate/replay behavior, DLQ authorization, and recovery drills have not been executed in this exploration. |
| UX, quality, and operations (P1) | **Confirmed gaps; scope-specific validation required** | The card UI has clear-PIN forms and modal markup without dialog/focus semantics. `main` has 14 frontend specs, CI build/test/secret-scan/Docker build, and OpenTelemetry instrumentation, but no E2E suite, accessibility evidence, SAST/SCA/SBOM/release controls, or tested SLO/backup/DR evidence were found. |

### Feature-Branch Disposition

- **Merged into `main`:** `feature/hsm-and-connectors` was merged by `15a98a0`; it is not an unmerged remedy. Its implementation remains a P0 maturity gap based on the current source above.
- **Unmerged:** `feature/observability-grafana` is two commits ahead of `main` (`f7db645`, `815991e`). It is a review candidate for observability evidence only; it must not be assumed correct or merged by this program without fresh review and verification.
- **Other unmerged branches:** disposition is **unknown**. No branch may be counted as a remediation until its diff, tests, and commercial impact are reviewed against `main`.

### Dependency Graph

```text
pilot capability + claim register
  -> commercial profile/demo containment + secrets evidence
  -> no-PAN/no-PIN boundary + sink redaction
  -> privileged access/action matrix + event/replay proof + pilot UX quality
  -> pilot SLO/runbooks + UAT/security evidence
  -> dedicated non-money-moving pilot

real HSM + one certified rail + PCI/control scope + HA/DR/load/reconciliation
  -> production-payment readiness decision
```

### Candidate Independently Releasable Changes

1. **commercial-scope-and-demo-containment** — approved claim register, product modes, production deny-by-default for demo/simulator/Swagger, and explicit unavailable states.
2. **vault-card-data-boundary** — remove browser PAN generation and clear-PIN API flows; replace issuance/PIN handling with a vault/HSM contract and regression checks.
3. **sensitive-data-governance** — sink inventory, field allowlist/redaction, retention/remediation, and negative tests across logs, traces, events, and audit records.
4. **privileged-operations-control** — verified action-level RBAC plus maker-checker for high-risk operations.
5. **eventing-operability-proof** — durable consumer/idempotency/replay/DLQ contracts, failure tests, monitoring, and runbooks.
6. **pilot-operator-experience** — selected pilot journey, accessible responsive UI, error taxonomy, frontend quality gate, and observability.
7. **one-rail-and-hsm-production-gate** — separate discovery and implementation for a contracted HSM and one certified rail; never bundle it into the pilot unless the pilot explicitly processes live payments.
8. **production-evidence-pack** — SLO, capacity, backup/restore, DR/failover, reconciliation, security assessment, and regulatory/control evidence.

### Approaches

1. **Production-switch-first**
   - Pros: single end-state narrative.
   - Cons: blocks commercial learning on HSM, certification, PCI scope, and operational evidence; high regulatory and delivery risk.
   - Effort: High.

2. **Pilot-first with hard product boundaries** — recommended.
   - Pros: saleable only after a bounded non-CDE/non-money-moving scope is made secure, observable, and auditable; produces evidence before rail/HSM commitments.
   - Cons: requires saying no to switch-replacement claims initially and selecting one customer problem.
   - Effort: Medium for pilot; High for later payment production.

### Pilot and Production Gates

**Pilot gate:** select one capability (recommended: auditability/operational visibility or collections); use synthetic/tokenized data only; close the applicable P0 data, demo-containment, secrets, and claim controls; accept relevant P1 authorization, eventing, UX, CI/CD, and operability evidence; document customer scope, KPIs, support, and exclusions.

**Production-payment gate:** all pilot controls plus a vendor-integrated HSM that never releases a clear PIN, one certified rail with approved mappings and reconciliation, validated PCI/regulatory scope, independent security evidence, HA/backup/restore/DR/load proof, and signed production operating model. No source-only review can satisfy these gates.

### Material Unknowns

- The target client's first pilot capability, data classification, hosting model, and success KPIs.
- Deployed environment configuration, historical secret exposure/rotation, and actual external attack surface.
- HSM vendor/protocol/key ceremony and partner rail certification requirements.
- Runtime evidence: build/test status, coverage, performance, failover, backup/restore, security assessment, and accessibility/usability results.
- Regulatory/commercial obligations for the precise Ecuador operating model.

### Recommendation

Proceed to a proposal for **pilot-first commercial readiness**, using `main` as the sole code baseline. Make the first SDD slice `commercial-scope-and-demo-containment`, then the card-data boundary and sensitive-data governance before exposing any commercial environment. Keep one-rail/HSM production work as a separately gated capability; do not merge or credit feature branches without review.

### Ready for Proposal

**Yes**, for the umbrella program. Before implementation, the user must choose the first pilot capability; the proposal should create a phased program and the follow-on specs should remain independently releasable.
