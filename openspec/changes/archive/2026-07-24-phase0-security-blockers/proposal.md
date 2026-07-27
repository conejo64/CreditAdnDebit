# Proposal: Phase 0 — Security Blockers

## Intent

A pre-commercialization security audit of ZitronSystem found CRITICAL gaps that would fail a PCI DSS audit on day one. These are not enhancements — they are the blockers that make the platform ineligible for production card data. Nothing else in the commercialization roadmap ships until these land.

Concretely, the audit confirmed the following against current code:

1. **Live cryptographic keys and DB credentials are committed to git.** `backend/services/CardVault/src/CardVault.Api/appsettings.Development.json` contains real AES-256-GCM vault keys (`Vault:Keys.k1` = `G64aK3Q44+yrGd5Mjgkq2D/4TDedO3dRwzjIOHsa11Q=`, `k2` = `4cnKCNXOU7qpb4JEVUW/FBmRdW9azcXIK/tkOj/gOaY=`), Postgres and SQL Server Identity connection strings with inline passwords, and the seed admin credentials `admin@demo.com` / `Admin1234!`. `backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.Development.json` contains `ApiKey: "dev-admin-key"`. Anyone with repo history has the keys that protect tokenized PANs. (PCI DSS 3.2.1, 3.5.)

2. **PINs are hashed with unsalted SHA-256.** `PinService.HashPin` (`CardVault.Application/Services/PinService.cs`) computes `SHA256.ComputeHash(pin)` with no salt and stores the Base64 result — the code even carries the comment "Simple hash for demo (in prod use Salt + Arg2 or similar)". A 4-digit PIN space is trivially rainbow-tableable; an identical PIN yields an identical hash across all cards. (PCI DSS 8.2.4, 3.5.)

3. **JWTs live in browser `localStorage`.** `frontend/src/app/core/auth.service.ts` stores both access and refresh tokens in `localStorage`, readable by any injected script. There is no CSP, no `X-Content-Type-Options`, no `X-Frame-Options`. A single XSS foothold exfiltrates a long-lived refresh token. (PCI DSS 6.5.)

4. **The ISO 8583 TCP channel defaults to plaintext.** `TcpIsoClientOptions.UseTls` defaults to `false`. Cardholder-bearing ISO messages (PAN, Track2, PIN block) can traverse the wire unencrypted, and nothing prevents a plaintext production deployment. (PCI DSS 4.1.)

5. **A default admin and a hardcoded admin API key ship in the box.** `Program.cs` seeds `admin@demo.com` / `Admin1234!` with `??` fallbacks that survive even if the appsettings values are removed; IsoSwitch trusts `ApiKey: "dev-admin-key"`. A known credential on a production host is a full compromise. (PCI DSS 2.1, 8.2.)

6. **Nothing stops secrets from being re-committed.** There is no secret scanning in CI or at commit time, so item 1 can silently regress the moment it is fixed.

Success for this phase means: no secret material in the repo or its history; PINs verifiable only through a salted, cost-parameterized KDF; browser sessions carried in `HttpOnly` cookies behind security headers; the ISO channel encrypted by default and unable to run plaintext in production; no default admin or hardcoded API key on a production host; and a CI + pre-commit gate that fails the pipeline on any newly committed secret.

This phase is the gate. It is delivered as **chained PRs, stacked-to-main** — one reviewable slice per item (SEC-01 … SEC-06) — so each blocker lands, is reviewed, and merges independently without a single oversized diff.

## Scope

### In Scope

Six independently shippable slices. Each is a self-contained PR.

- **SEC-01 — Purge and rotate all committed secrets.**
  - Move every secret out of `appsettings.Development.json` (both CardVault and IsoSwitch) to an environment-variable / secrets-manager configuration provider. Follow the already-established repo convention where secrets are read from environment variables and are deliberately absent from options types (see `SendGridOptions` / `MovistarOptions`: "`ApiKey` is a secret and is intentionally NOT a property here").
  - Rotate all currently committed keys and credentials: generate new vault keys, new DB passwords, new seed credentials.
  - Re-encrypt stored tokenized PANs under the new vault key via the existing vault key-rotation / re-encryption workflow (`vault-and-pci` capability), then revoke the old key ids (`k1`, `k2`) so they can never decrypt.
  - Purge the leaked values from git history (history rewrite / BFG-style scrub) so the keys are unrecoverable from the repo.
  - Provide a committed non-secret template (e.g. `.env.example` / appsettings skeleton with empty or placeholder values) documenting which variables an operator must supply.

- **SEC-02 — Salted, cost-parameterized PIN hashing (Argon2id interim).**
  - Replace unsalted SHA-256 in `PinService` with Argon2id using a per-PIN random salt and tuned memory/iteration/parallelism cost parameters. Persist the algorithm id, parameters, and salt alongside the hash so parameters can evolve without a breaking migration.
  - Define the transition strategy for existing unsalted-SHA-256 `PinHash` values: they cannot be reversed, so re-hash on next successful verification (verify-then-upgrade) or force a PIN reset — the mechanism is decided at spec/design, but no card is left verifiable only by the old scheme.
  - This is the **interim** control. The definitive control moves PIN verification into an HSM (never hashing the PIN in application memory) — that HSM work is **Phase 1** and is out of scope here (forward reference below).

- **SEC-03 — JWT in `HttpOnly` cookies + security headers (backend + frontend).**
  - **Backend (CardVault):** issue access and refresh tokens as `HttpOnly; Secure; SameSite` cookies instead of (or in addition to) the JSON body; accept the token from the cookie in the auth pipeline; support cookie-based refresh and logout (cookie clearing). Add response security headers: `Content-Security-Policy`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` (and the corresponding `frame-ancestors` in CSP).
  - **Frontend (Angular):** remove access/refresh token storage from `localStorage` in `auth.service.ts`; adapt the HTTP interceptor to rely on `withCredentials` cookies rather than an `Authorization` header pulled from storage; adapt refresh and logout flows to the cookie model. Client-side JWT-expiry parsing (`isTokenExpired`) is reworked or removed since the token is no longer readable by JS.
  - This slice **spans backend and frontend**; it is called out explicitly so reviewers expect both surfaces in one coherent change.

- **SEC-04 — TLS enforced on the ISO 8583 TCP channel.**
  - Change `TcpIsoClientOptions.UseTls` default to `true`.
  - Fail startup in `Production` when TLS is disabled for a non-loopback acquirer host. Plaintext remains permitted only for localhost / loopback simulators. `AllowInvalidCert` stays gated to `Development` (already the case in `Program.cs`).

- **SEC-05 — Remove default admin seed and hardcoded admin API key.**
  - Seed the admin user only when the environment is `Development`; remove the hardcoded `?? "admin@demo.com"` / `?? "Admin1234!"` fallbacks so a missing config never silently produces a known admin. In non-Development environments, admin provisioning is a controlled, explicit operation (no auto-seed).
  - Remove the default `dev-admin-key` from IsoSwitch; require the admin API key to be supplied via configuration with fail-fast validation, and reject the known dev placeholder (consistent with the existing `security-hardening` fail-fast pattern for `Tokenization:Secret` / `Jwt:SigningKey`).

- **SEC-06 — Secret scanning in CI + pre-commit.**
  - Integrate a secret scanner (gitleaks or TruffleHog) as a job in `.github/workflows/ci.yml` that fails the pipeline when a secret is detected, and as a pre-commit hook so a secret is caught before it ever enters history.
  - This slice protects SEC-01 from regressing. Ship it so the gate exists for the remainder of the phase.

### Out of Scope

Explicitly deferred to **Phase 1 and later**, with forward references so `sdd-design` does not pull them in:

- **HSM-backed PIN verification** — the definitive replacement for SEC-02's interim Argon2id. Phase 1.
- **mTLS / client-certificate authentication and IP allowlisting on the ISO 8583 TCP channel (port 7000)** — SEC-04 delivers server-side TLS; mutual TLS and acquirer-cert provisioning are Phase 1 (also noted out-of-scope in the existing `security-hardening` delta).
- **Network/PKI certificate provisioning and rotation infrastructure** (acquirer certs, internal CA) — Phase 1.
- **Formal PCI DSS scoping / network segmentation / ROC evidence packaging** — later phase.
- **MFA end-to-end wiring** — the frontend already surfaces "MFA required" but the flow is not connected; not part of this phase.
- **Password recovery flow** — owned by `secure-user-registration` / `identity-and-access` IAM-PR-* requirements, not re-opened here.
- **Broader CD / registry push / cloud deployment** — owned by `cicd-packaging`; SEC-06 only adds a secret-scanning job to the existing `ci.yml`, it does not redesign the pipeline.

## Capabilities

### Modified Capabilities

- **`security-hardening`** — primary home for the cross-cutting startup and channel controls. New / extended SHALL requirements for:
  - SEC-01 secrets purged from committed configuration; startup reads secrets only from environment/secret-manager providers; committed config carries no secret material.
  - SEC-04 ISO TCP channel defaults to TLS-on and fails startup in Production when TLS is disabled for a non-loopback host (supersedes the "out-of-scope" note for TCP TLS in the current delta — server-side TLS is now in scope; mTLS remains deferred).
  - SEC-05 IsoSwitch admin API key must be operator-supplied and fail-fast validated; the `dev-admin-key` placeholder is rejected (mirrors existing SEC-1/SEC-2/SEC-3 fail-fast pattern).
  - SEC-03 response security headers (CSP, `X-Content-Type-Options`, `X-Frame-Options`) present on CardVault responses.

- **`identity-and-access`** — auth-surface behavior:
  - SEC-03 access/refresh tokens delivered and accepted via `HttpOnly; Secure; SameSite` cookies; the seeded-development-user requirement is narrowed so administrative seeding occurs only in `Development` (SEC-05).

- **`vault-and-pci`** — cardholder-data cryptography:
  - SEC-01 tokenized PANs re-encrypted under a rotated vault key and the old key ids revoked, using the existing rotation / re-encryption + `VaultKeyRotated` / `VaultReencryptionBatchCompleted` audit contract.
  - SEC-02 PIN verification uses a salted, cost-parameterized KDF (Argon2id interim); no PIN is stored or verifiable via unsalted SHA-256 after transition; PIN material is never logged.

- **`cicd-packaging`** — pipeline gate:
  - SEC-06 `.github/workflows/ci.yml` includes a secret-scanning job that fails the run on detection; a pre-commit hook enforces the same locally.

### New Capabilities

- None. All six items are deltas on the four existing capabilities above. If `sdd-spec` finds a control that does not fit any of these cleanly, a focused capability may be proposed at that time, but the intent is to extend, not duplicate.

## Approach

**Why phased, and why now.** These six are grouped because they share one gate: PCI eligibility. They are separated into independent slices because they touch different bounded contexts and surfaces (crypto config, PIN KDF, browser session, network channel, provisioning, pipeline), and a monolithic "security" PR would be unreviewable and un-rollbackable. Stacked-to-main chaining lets each control land, be verified against its spec, and merge on its own evidence.

**Ordering rationale.** SEC-06 (secret scanning) should land early — ideally first or alongside SEC-01 — so the moment SEC-01 purges and rotates secrets, the gate is already in place to prevent regression. SEC-01 itself is sequenced with care: rotate keys and re-encrypt PANs *before* revoking the old key ids, so no tokenized data is orphaned. SEC-02's transition must not leave any card verifiable only by the old unsalted hash. SEC-03 is the one cross-surface slice (backend cookie issuance + Angular interceptor) and is kept as a single coherent PR so the token model never straddles two schemes across a merge boundary.

**Consistency with existing patterns.** SEC-01 and SEC-05 follow conventions the codebase already established: secrets-as-environment-variables (the notification `*Options` types deliberately omit secret properties) and fail-fast startup validation that rejects known DEV placeholders (`security-hardening` SEC-1/2/3). SEC-01's re-encryption reuses the vault rotation workflow rather than inventing a new one. This keeps the phase additive and low-surprise.

**Bounded-context discipline.** CardVault owns SEC-01 (vault keys, identity DB, seed admin), SEC-02 (PIN KDF), SEC-03 backend (cookie issuance), and SEC-05 seed. IsoSwitch owns SEC-04 (ISO TCP TLS) and SEC-05 (admin API key). The Angular frontend owns SEC-03's client half. SEC-06 is repo-wide CI/tooling. No slice crosses a context boundary except SEC-03, which is intentionally a single backend+frontend session-model change.

## Affected Areas

| Area | Slice | Impact | Description |
|------|-------|--------|-------------|
| `backend/services/CardVault/src/CardVault.Api/appsettings.Development.json` | SEC-01 | Modified | Strip vault keys, connection-string passwords, seed credentials; leave non-secret skeleton |
| `backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.Development.json` | SEC-01, SEC-05 | Modified | Strip `ApiKey`; move to env/secret provider |
| `backend/.env.example` (or equivalent) | SEC-01 | New | Documents required secret variables (non-secret placeholders) |
| git history | SEC-01 | Rewrite | Purge leaked secret values from history |
| `backend/services/CardVault/src/CardVault.Application/Services/PinService.cs` | SEC-02 | Modified | Replace unsalted SHA-256 with Argon2id + salt + cost params |
| CardVault PIN persistence (card `PinHash` and new algo/params/salt fields) | SEC-02 | Modified | Store algorithm id, parameters, salt; EF migration + transition strategy for existing hashes |
| `frontend/src/app/core/auth.service.ts` | SEC-03 | Modified | Remove token `localStorage`; move to cookie model |
| Angular HTTP interceptor | SEC-03 | Modified | `withCredentials`; drop `Authorization`-from-storage |
| CardVault auth pipeline / token issuance (`Program.cs`, auth controller) | SEC-03 | Modified | Issue + accept `HttpOnly; Secure; SameSite` cookies; add CSP / `X-Content-Type-Options` / `X-Frame-Options` |
| `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/Net/TcpIsoClientOptions.cs` | SEC-04 | Modified | Default `UseTls = true` |
| IsoSwitch `Program.cs` / options validation | SEC-04, SEC-05 | Modified | Fail-fast in Production when TLS disabled for non-loopback host; require operator admin API key, reject `dev-admin-key` |
| `backend/services/CardVault/src/CardVault.Api/Program.cs` | SEC-05, SEC-01 | Modified | Seed admin only in Development; remove `?? "admin@demo.com"` / `?? "Admin1234!"` fallbacks |
| `.github/workflows/ci.yml` | SEC-06 | Modified | Add secret-scanning job (gitleaks/TruffleHog) that fails on detection |
| Pre-commit hook config (e.g. `.pre-commit-config.yaml` / git hook) | SEC-06 | New | Local secret-scan gate |
| `openspec/specs/security-hardening/spec.md` | SEC-01/03/04/05 | Modified | New SHALL requirements (secrets purge, ISO TLS default, admin key fail-fast, security headers) |
| `openspec/specs/identity-and-access/spec.md` | SEC-03/05 | Modified | Cookie-based token delivery; Development-only admin seeding |
| `openspec/specs/vault-and-pci/spec.md` | SEC-01/02 | Modified | Re-encryption under rotated key + old-key revocation; salted-KDF PIN hashing |
| `openspec/specs/cicd-packaging/spec.md` | SEC-06 | Modified | Secret-scanning CI job + pre-commit gate |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Revoking old vault keys before re-encryption completes orphans tokenized PANs (unrecoverable) | Medium | Strict SEC-01 ordering: rotate → re-encrypt all records → verify → only then revoke `k1`/`k2`; reuse the existing batch re-encryption workflow with its completion audit event |
| Git history rewrite breaks forks / open branches / CI refs | Medium | Coordinate the history scrub as a discrete, announced step; treat rotation (not just scrub) as the real remediation — assume the old keys are already compromised and are being replaced regardless |
| Existing unsalted-SHA-256 PINs cannot be migrated in place | High (inherent) | Verify-then-upgrade on next successful PIN entry, or forced PIN reset; spec/design decides, but no card remains verifiable only by the old scheme, and old hashes are removed once upgraded |
| SEC-03 cookie switch breaks the SPA if backend and frontend halves drift | Medium | Ship SEC-03 as one coherent backend+frontend PR; `SameSite`/`Secure`/CORS-with-credentials configured together; refresh + logout reworked in the same slice |
| `SameSite`/`Secure` cookie choice conflicts with cross-origin dev setup (`localhost:4200` → API) | Medium | Choose `SameSite` value and CORS `AllowCredentials` deliberately at design; keep dev flow working (Development-only relaxations, never in Production) |
| SEC-04 default `UseTls=true` breaks existing localhost simulators | Low | Plaintext explicitly allowed for loopback; fail-fast only applies to non-loopback hosts in Production |
| Secret scanner false positives block the pipeline | Low | Tune allowlist / baseline for known non-secret placeholders; the `.env.example` uses obvious placeholders |
| Argon2id cost params too aggressive → PIN verification latency on the auth path | Low | Tune memory/iteration/parallelism against the auth-path budget; parameters are stored per-hash so they can be adjusted forward |

## Rollback Plan

- **Per-slice, independent.** Because delivery is stacked-to-main with one PR per SEC item, each slice reverts on its own without unwinding the others.
- **SEC-01:** rotation is not reversible (the old keys are treated as compromised); a rollback of the *code* change reverts config wiring but operators keep the rotated keys. Re-encryption has an audit trail via the existing vault events.
- **SEC-02:** the KDF change is forward-only for upgraded hashes; a code rollback would reintroduce a vulnerable path and is therefore not a real option once shipped — this is called out so the slice is treated as a one-way door.
- **SEC-03:** feature-flag or config toggle can keep the legacy header-token path available during cutover if design chooses a dual-accept window; otherwise revert both halves together.
- **SEC-04:** revert the `UseTls` default; operational config can still force TLS on.
- **SEC-05:** revert restores the seed fallback (undesirable) — prefer forward fix; the change itself is low-risk.
- **SEC-06:** the CI job / hook can be disabled independently; it introduces no runtime behavior.

## Dependencies

- Existing `vault-and-pci` key-rotation and re-encryption workflow (SEC-01 re-encryption reuses it).
- Existing `security-hardening` fail-fast startup-validation pattern (SEC-04, SEC-05 extend it).
- Existing `cicd-packaging` `ci.yml` (SEC-06 adds a job to it).
- Outbox + audit pipeline for `VaultKeyRotated` / `VaultReencryptionBatchCompleted` (SEC-01 re-encryption evidence).

## Unlocks (downstream)

- **Phase 1** (HSM PIN verification, ISO mTLS + acquirer certs, PKI provisioning) — blocked on this gate; SEC-02 and SEC-04 lay the interim controls those replace.
- **Commercialization** — the platform cannot begin a PCI DSS assessment until these six blockers are closed.

## Success Criteria

- [ ] No secret material (vault keys, DB passwords, admin credentials, API keys) appears in any committed file or in git history.
- [ ] CardVault and IsoSwitch read all secrets exclusively from environment / secret-manager providers; a committed `.env.example` documents the required variables.
- [ ] Committed vault keys `k1`/`k2` are rotated, tokenized PANs are re-encrypted under the new key, and the old key ids are revoked and cannot decrypt.
- [ ] `PinService` verifies PINs via Argon2id with a per-PIN salt and tuned cost parameters; no PIN is stored or verifiable via unsalted SHA-256; PIN material never appears in logs.
- [ ] Access and refresh tokens are delivered and accepted as `HttpOnly; Secure; SameSite` cookies; `localStorage` no longer holds token material; CSP, `X-Content-Type-Options`, and `X-Frame-Options` are present on CardVault responses.
- [ ] The Angular SPA authenticates, refreshes, and logs out against the cookie model end-to-end.
- [ ] `TcpIsoClientOptions.UseTls` defaults to `true`; IsoSwitch fails startup in Production when TLS is disabled for a non-loopback host; loopback simulators still run plaintext.
- [ ] No admin user is auto-seeded outside Development; no `?? "admin@demo.com"` / `?? "Admin1234!"` fallback remains; IsoSwitch rejects the `dev-admin-key` placeholder and requires an operator-supplied key with fail-fast validation.
- [ ] `.github/workflows/ci.yml` fails when a secret is detected, and a pre-commit hook catches secrets locally before they enter history.
- [ ] Each SEC-0x control ships as an independent, reviewable, stacked-to-main PR.
