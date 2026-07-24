# SDD Archive Report: phase0-security-blockers

**Date:** 2026-07-24
**Change:** phase0-security-blockers
**Status:** ARCHIVED (all 6 SEC slices merged to main, tests passing 705/705, verify report PASS)
**Artifact Store:** Hybrid (OpenSpec files + Engram mirror)

---

## Executive Summary

The `phase0-security-blockers` SDD change is complete, verified, and archived. All six security hardening slices (SEC-01 through SEC-06) are merged to `main` at commit `4a67dd6`, with passing test suite (705/705 tests: CardVault 624, IsoSwitch 63, IsoAudit 18) and a PASS verification report. All four affected capability specs have been merged with their delta specs. The change folder has been moved to the archive directory `openspec/changes/archive/2026-07-24-phase0-security-blockers/`.

---

## Change Overview

| Item | Value |
|------|-------|
| **Change Name** | phase0-security-blockers |
| **Phase** | Phase 0: PCI DSS eligibility gate |
| **PRs Merged** | 6 (SEC-01 through SEC-06, all stacked-to-main to `main`) |
| **Final Commit** | `4a67dd6` (PR #8-#13, all merged) |
| **Capabilities Modified** | 4: security-hardening, identity-and-access, vault-and-pci, cicd-packaging |
| **Tests Passing** | 705/705 (CardVault 624, IsoSwitch 63, IsoAudit 18) |
| **Build Status** | Clean (0 errors, pre-existing warnings only) |
| **Verification** | PASS (0 CRITICAL, 1 WARNING pre-existing/non-blocking, 4 SUGGESTIONs) |

---

## SDD Artifacts

### Engram Observation IDs (for traceability)

All artifacts retrieved from engram and mirrored in this archive:

| Artifact | Topic Key | Observation ID | Timestamp |
|----------|-----------|---|-----------|
| Proposal | `sdd/phase0-security-blockers/proposal` | 3256 | 2026-07-08 19:27:02 |
| Spec | `sdd/phase0-security-blockers/spec` | 3257 | 2026-07-08 19:30:55 |
| Design | `sdd/phase0-security-blockers/design` | 3258 | 2026-07-08 19:33:09 |
| Tasks | `sdd/phase0-security-blockers/tasks` | 3259 | 2026-07-08 19:42:45 |
| Apply Progress | `sdd/phase0-security-blockers/apply-progress` | 3264 | 2026-07-09 21:14:17 |
| Verify Report | `sdd/phase0-security-blockers/verify-report` | 3975 | 2026-07-24 17:42:22 |
| Archive Report | `sdd/phase0-security-blockers/archive-report` | (this save) | 2026-07-24 |

### OpenSpec Artifacts

All files archived at `openspec/changes/archive/2026-07-24-phase0-security-blockers/`:

- `proposal.md` — Full SDD proposal pinning intent, scope, capabilities, risks, rollback, and success criteria
- `design.md` — Architecture decisions (SEC-01 through SEC-06), one decision section per slice
- `tasks.md` — Implementation tasks checklist (PR 1-6, 6.1-6.10 all checked; non-blocking X.1 open by design)
- `verify-report.md` — Final verification audit against merged code on main @ 4a67dd6, test execution evidence

#### Delta Specs (merged into main specs)

All 4 capability delta specs have been merged into their corresponding main specs at `openspec/specs/{capability}/spec.md`:

1. **security-hardening** — ADDED SEC-9 through SEC-12
   - SEC-9: No Secret Material in Committed Configuration — Env-Only Secret Loading
   - SEC-10: ISO 8583 TCP Channel — TLS Default-On and Production Fail-Fast
   - SEC-11: IsoSwitch Admin API Key — Operator-Supplied, Fail-Fast, Rejects DEV Placeholder
   - SEC-12: CardVault Response Security Headers

2. **identity-and-access** — MODIFIED JWT-Based Authentication (Development-only seeding) + ADDED Cookie-Based Token Delivery
   - Narrowed admin seeding to Development environment (SEC-05)
   - ADDED full Cookie-Based Token Delivery requirement (SEC-03)

3. **vault-and-pci** — ADDED Re-Encryption Under Rotated Key With Old-Key Revocation + Salted, Cost-Parameterized PIN Hashing
   - SEC-01 (Re-Encryption): Uses existing vault rotation machinery, orphan-proof COUNT gate
   - SEC-02 (PIN KDF): Argon2id with per-PIN salt, verify-then-upgrade migration strategy

4. **cicd-packaging** — ADDED CICD-13 and CICD-14
   - CICD-13: Secret-Scanning CI Job That Fails on Detection (gitleaks)
   - CICD-14: Pre-Commit Secret-Scanning Hook

---

## Verification Summary

### Test Results

Verified via isolated per-project runs (avoiding a known environmental flake in the full-solution parallel test load):

```
CardVault.Tests    624/624 PASS
IsoSwitch.Tests     63/63 PASS
IsoAudit.Tests      18/18 PASS
─────────────────────────────
Total              705/705 PASS
```

All test counts match the apply-progress exact counts. Build clean: `dotnet build backend/CardSwitchPlatform.sln -c Release` succeeded with 0 errors and only pre-existing warnings (NU1902, SYSLIB0053, nullability).

### Spec-to-Code Verification

All ADDED/MODIFIED requirements in the four capability deltas were spot-checked against real code:

| Requirement | Evidence |
|---|---|
| SEC-9 (no secret material in config) | CardVault/IsoSwitch appsettings.Development.json verified empty of secrets; backend/deploy/.env.example documents vars |
| SEC-10 (ISO TCP TLS default-on) | TcpIsoClientOptions.cs UseTls=true; TcpIsoClientOptionsValidator enforces fail-fast |
| SEC-11 (admin API key fail-fast) | AdminApiKeyOptionsValidator.cs with Forbidden array including "dev-admin-key" |
| SEC-12 (security headers) | SecurityHeadersMiddleware.cs sets X-Content-Type-Options nosniff, X-Frame-Options DENY, CSP |
| Cookie-Based Token Delivery | AuthCookieWriter.cs HttpOnly=true, Secure=true, SameSite=Lax; JWT bearer OnMessageReceived reads cv_at cookie |
| Non-Development admin seeding | CardVault.Api Program.cs user-seed block gated to IsDevelopment() |
| Re-Encryption + Revocation | VaultRevocationGateTests present and passing; orphan-proof COUNT gate verified |
| Salted PIN Hashing | PinService.cs uses Argon2id; PinHashAlgorithm/Salt/Params columns stored; VerifyLegacySha256 legacy branch |
| CICD-13 (secret-scan job) | .github/workflows/ci.yml includes secret-scan job with gitleaks/gitleaks-action@v2 |
| CICD-14 (pre-commit hook) | .pre-commit-config.yaml present and configured |

### Task Completion Gate

All implementation tasks for SEC-01 through SEC-06 are marked `[x]` in tasks.md (checked). Operator-executed runbook tasks (SEC-01 steps 2.6, 2.8, 2.11, 2.13, 2.15 — live key rotation, git history scrub) are correctly marked `[~]` to indicate they are out-of-band human actions, not part of the code PR. The single remaining unchecked task (X.1, vault-and-pci spec wording clarification) is non-blocking and documentation-only.

---

## Findings and Known Open Items

### CRITICAL
None.

### WARNING
1. **(Pre-existing, non-blocking)** Task X.1: vault-and-pci spec wording ambiguity on "exactly one event per batch". The base spec's "PCI-Safe Audit Events" requirement can be misread as including the terminal no-op batch, which emits no event by design. Already flagged in tasks.md as non-blocking (does not affect shipped behavior); recommend a small spec-wording follow-up PR.

### SUGGESTIONS
1. **IsoSwitch Admin:ApiKey consumer:** The `Admin:ApiKey` currently has no request-time consumer. SEC-05 delivers fail-fast validation only. This is explicitly scoped as a Phase 0 caveat (not a gap). Wiring actual request-time authentication is a Phase 1 follow-on.
2. **SEC-03 SameSite production topology:** Design assumes SPA and API share a registrable domain in production (SameSite=Lax). Recommend confirming actual production deployment topology before production rollout, since a split-domain deployment would require revisiting to SameSite=None. Documented in the design.md assumptions section.
3. **PR 6 merge confirmation:** Confirmed present in git log (`4a67dd6`, PR #13).
4. **Transient local build OOM:** A one-off `OutOfMemoryException` during the verify phase was traced to stale MSBuild/VBCSCompiler build-server processes in the local environment, not a code or CI issue.

---

## Scope Boundaries (Preserved as Outstanding)

The following intentional scope boundaries are carried forward as documented in the proposal/design/tasks:

### Operator-Executed Tasks (SEC-01)

Tasks 2.6, 2.8, 2.11, 2.13, 2.15 in tasks.md are runbook-documented operations that require human execution against a live environment:

- **2.6:** Rotate active key to k3 (requires operational authority to touch production vault config)
- **2.8:** Run re-encryption workflow until COUNT(TokenVault WHERE KeyId != active) == 0 (requires ongoing monitoring)
- **2.11:** Verify orphan gate before revocation (critical verification before irreversible step)
- **2.13:** Revoke old key ids k1/k2 (irreversible step, must be human-approved)
- **2.15:** Scrub git history with `git filter-repo` (irreversible, announced, coordinated separately)

These are correctly left unchecked in tasks.md because they are **not PR implementation tasks** — they are post-merge operational steps documented in `backend/SECRETS-ROTATION-RUNBOOK.md` for the security/operations team to execute. The archive report carries them forward as **outstanding operational follow-ups** requiring explicit coordination.

### Non-Blocking Spec Clarification (X.1)

Task X.1 (vault-and-pci "exactly one event per batch" wording) is documented as non-blocking in tasks.md. The actual code and tests already implement and verify the correct behavior (the terminal zero-remaining batch emits no event by design). This is a documentation-only follow-up that does not block any shipped functionality.

### Phase 1 Deferred Items

The following are explicitly out of scope for Phase 0, per proposal/design, and are deferred to Phase 1:

- **HSM-backed PIN verification** — SEC-02 delivers the interim Argon2id control; HSM is Phase 1
- **mTLS / client-certificate authentication + IP allowlisting** — SEC-04 delivers server-side TLS; mTLS is Phase 1
- **IsoSwitch Admin:ApiKey request-time authentication** — SEC-05 delivers fail-fast validation; consumer middleware is Phase 1
- **Certificate provisioning and rotation infrastructure** — Phase 1+

---

## Archive Verification Checklist

- [x] Main specs updated with all 4 delta specs merged (security-hardening, identity-and-access, vault-and-pci, cicd-packaging)
- [x] All ADDED/MODIFIED requirements verified against merged code on main @ 4a67dd6
- [x] All 6 PR slices (SEC-01..SEC-06) merged to main; no unmerged branches
- [x] Test suite passing (705/705, via isolated per-project runs — see Test Results)
- [x] Build clean (0 errors)
- [x] Verify report PASS (0 CRITICAL, 1 non-blocking WARNING, 4 SUGGESTIONs)
- [x] No stale unchecked implementation tasks (all SEC-01..SEC-06 tasks marked `[x]`)
- [x] Operator runbook tasks (2.6, 2.8, 2.11, 2.13, 2.15) correctly marked `[~]` with runbook reference
- [x] Non-blocking spec clarification (X.1) correctly left open by design
- [x] Change folder moved to archive at `openspec/changes/archive/2026-07-24-phase0-security-blockers/`
- [x] Archive report written to both OpenSpec and Engram with full observation ID trail

---

## Next Steps

The change is complete and archived. No further action on phase0-security-blockers is required. The outstanding operational tasks (SEC-01 key rotation, git history scrub) are documented in the runbook and are outside the SDD phase scope.

### Recommended Follow-Ups (Phase 1+)

1. Execute SEC-01 operational runbook tasks (key rotation, re-encryption, revocation, history scrub) per `backend/SECRETS-ROTATION-RUNBOOK.md` at a coordinated operational window
2. File spec-wording follow-up (task X.1) if desired, but not blocking
3. Confirm SEC-03 production deployment topology (SPA/API same vs. different registrable domain) before production rollout
4. Implement IsoSwitch Admin:ApiKey request-time authentication consumer (Phase 1)
5. Begin Phase 1 work (HSM PIN verification, ISO mTLS, PKI provisioning, etc.)

---

## Artifact Locations

All artifacts for this change are preserved in the archive:

**File System (OpenSpec):**
```
openspec/changes/archive/2026-07-24-phase0-security-blockers/
├── proposal.md (full SDD proposal)
├── design.md (architecture decisions SEC-01 through SEC-06)
├── tasks.md (implementation task checklist)
├── verify-report.md (verification audit)
├── archive-report.md (this file)
└── specs/ (delta specs, preserved as-merged)
    ├── cicd-packaging/spec.md
    ├── identity-and-access/spec.md
    ├── security-hardening/spec.md
    └── vault-and-pci/spec.md
```

**Engram (persistent memory):**
- `sdd/phase0-security-blockers/proposal` (ID 3256)
- `sdd/phase0-security-blockers/spec` (ID 3257)
- `sdd/phase0-security-blockers/design` (ID 3258)
- `sdd/phase0-security-blockers/tasks` (ID 3259)
- `sdd/phase0-security-blockers/apply-progress` (ID 3264)
- `sdd/phase0-security-blockers/verify-report` (ID 3975)
- `sdd/phase0-security-blockers/archive-report` (this save)

**Main Specs (merged):**
- `openspec/specs/security-hardening/spec.md` (SEC-1..SEC-8 + SEC-9..SEC-12)
- `openspec/specs/identity-and-access/spec.md` (MODIFIED JWT scenario + ADDED Cookie-Based Token Delivery)
- `openspec/specs/vault-and-pci/spec.md` (ADDED Re-Encryption + Salted PIN Hashing)
- `openspec/specs/cicd-packaging/spec.md` (CICD-1..CICD-12 + CICD-13..CICD-14)

---

## SDD Cycle Summary

| Phase | Status | Artifact | Observation ID |
|-------|--------|----------|---|
| Proposal | Complete | sdd/phase0-security-blockers/proposal | 3256 |
| Spec | Complete | sdd/phase0-security-blockers/spec | 3257 |
| Design | Complete | sdd/phase0-security-blockers/design | 3258 |
| Tasks | Complete | sdd/phase0-security-blockers/tasks | 3259 |
| Apply | Complete | sdd/phase0-security-blockers/apply-progress | 3264 |
| Verify | Complete (PASS) | sdd/phase0-security-blockers/verify-report | 3975 |
| Archive | Complete | sdd/phase0-security-blockers/archive-report | (this) |

**SDD Cycle: CLOSED** — All phases complete, change merged, verified, and archived.

---

**Archived by:** sdd-archive (executor)
**Archive date:** 2026-07-24
**Archive mode:** Hybrid (OpenSpec files + Engram mirror)
