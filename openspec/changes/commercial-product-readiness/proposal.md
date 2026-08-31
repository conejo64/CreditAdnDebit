# Proposal: CardTech Commercial Product Readiness

## Intent
Make ZitronSystem licensable to CardTech in Ecuador for regional expansion, without overstating product maturity. CardTech is the B2B channel and future operator; ZitronSystem retains its IP. This enables a 90-day pilot and evidence for a separately gated 12-month production path.

## Scope
### In Scope
- A claim register classifying each function as **verified**, **simulation**, or **roadmap**.
- A non-money-moving, synthetic/tokenized-data pilot, starting with `commercial-scope-and-demo-containment`.
- A core for future CardTech-managed SaaS and dedicated institutional deployment, with tenant, key, data, audit, SLA, and responsibility boundaries.
- Gates and a phased backlog for non-exclusive licensing, Ecuador exclusivity with commitments, and exceptional high-premium source transfer.

### Out of Scope
- Live PAN/PIN, PCI/network-certification claims, live switching, or production rail/HSM operation.
- Source transfer, regulatory authorization, pricing, and contract execution.
- Crediting unmerged branches as remediation.

## Capabilities
### New Capabilities
- `commercial-deployment-governance`: claim register, profiles, demo containment, pilot gates, and evidence classification.
- `cardtech-operator-readiness`: operating boundaries and readiness evidence for managed and dedicated profiles.

### Modified Capabilities
- `vault-and-pci`: prohibit browser PAN generation and clear-PIN flows in commercial deployments.
- `iso-switch-processing`: simulator/demo execution is non-commercial and unavailable by default outside approved demo environments.
- `security-hardening`: strengthen environment exposure, redaction, and secret-evidence requirements.
- `identity-and-access`: verify action-level controls; introduce maker-checker where risk warrants.
- `event-integration-and-observability`: make replay, DLQ, audit, and operator evidence production-gate criteria.

## Approach and Gates
Deliver independently releasable changes: demo containment; vault boundary; sensitive-data governance; privileged controls; eventing proof; pilot UX; rail/HSM gate; production evidence pack. Pilot requires agreed KPIs, no live cardholder data or money movement, P0 boundary/secret/demo controls, and UAT/security/operability evidence. Production payments additionally require a clear-PIN-safe vendor HSM, one certified rail, validated regulatory/control scope, independent security assessment, HA/DR/load/restore/reconciliation proof, and a signed operating model.

## Affected Areas
| Area | Impact |
|---|---|
| `backend/services/CardVault` | Card-data, issuance, authorization, audit boundaries |
| `backend/services/IsoSwitch` | Simulator, rail, audit, eventing, operations |
| `frontend` | Labels, safe journeys, accessibility |
| `backend/deploy`, CI/CD | Deployment profiles and evidence |

## Risks, Dependencies, and Rollback
Risk: claims outrun proof; mitigate with register and fail-closed profiles. Risk: delayed HSM/rail contracts; keep the gate independent. Dependencies: CardTech pilot/KPIs, hosting/data classification, HSM/rail partners, security and regulatory counsel. Rollback: feature flags and deny-by-default commercial profiles; retain demos only in isolated non-commercial environments; stop at a failed gate.

## Success Criteria
- [ ] CardTech demonstrates only accurately classified functions in a controlled pilot.
- [ ] Pilot production-like environments expose no demo routes, clear PAN/PIN flow, or unsupported claim.
- [ ] Every phase has verified exit evidence and rollback.
- [ ] Production payments are withheld until all production gates are proven and accepted.