# Design: Phase 0 — Security Blockers

## Architectural Position

This phase is a set of **six independent, additive controls** layered onto an existing .NET 9 / ASP.NET Core clean-architecture codebase with two bounded contexts (CardVault, IsoSwitch) and an Angular SPA. No new bounded context, no new capability, no service-boundary change. Each SEC item is a **stacked-to-main PR slice** — independently reviewable, independently revertable (with two explicit one-way-door exceptions noted below).

The overriding design principle is **fail-closed in Production, keep Development frictionless**. Every hardening either (a) is enforced only in non-Development environments, or (b) has a Development-only relaxation gated on `IHostEnvironment.IsDevelopment()`. This mirrors the pattern the codebase already established: `TokenizationOptionsValidator` / `JwtOptionsValidator` reject known DEV placeholders at startup, and `Program.cs` gates `AllowInvalidCert` to Development only.

### Global sequencing constraint (cross-slice, load-bearing)

SEC-06 (secret scanning) **lands first**. Then SEC-01 (purge + rotate). Reason: the instant SEC-01 rewrites history and rotates keys, the CI + pre-commit gate must already exist to stop a re-commit. SEC-02/03/04/05 are order-independent among themselves and can be developed in parallel branches, each stacked on main after SEC-01 merges. This is the only hard ordering edge; the rest of the graph is a fan-out.

```
SEC-06 (gate)  ─►  SEC-01 (purge+rotate)  ─►  { SEC-02, SEC-03, SEC-04, SEC-05 }  (parallel, each stacked-to-main)
```

---

## SEC-01 — Purge and rotate all committed secrets

### Decision: env-var configuration provider, NOT a secrets manager (this phase)

**Chosen:** Keep the existing `ASPNETCORE`/`DOTNET` environment-variable configuration provider (double-underscore nesting: `Vault__Keys__k1`, `ConnectionStrings__Postgres`, `Seed__AdminPassword`, `Admin__ApiKey`). Strip all secret values out of both `appsettings.Development.json` files, leaving non-secret structural skeleton. Ship a committed `backend/.env.example` documenting every required variable with obvious placeholders (`__REPLACE_ME__`).

**Rejected — cloud secrets manager (Azure Key Vault / AWS Secrets Manager / Vault by HashiCorp):** Correct long-term target, but it is a Phase-1+ infrastructure decision (the proposal explicitly defers PKI/secret-provisioning infra). Adopting it now couples this security-blocker slice to a cloud provider choice that has not been made, and the repo already standardized on env-vars — see `EnvironmentSendGridApiKeyProvider` / `EnvironmentMovistarApiKeyProvider` and the `*Options` types that deliberately omit secret properties (`SendGridOptions`, `MovistarOptions`). **Tradeoff:** env-vars are less auditable and don't rotate themselves, but they are the established convention, require zero new infra, and the configuration binding is provider-agnostic — swapping to a secrets manager later is a one-provider-registration change, not a rewrite. **Rationale:** consistency + smallest reversible surface for a blocker.

### Decision: exact ordered runbook — orphaning tokenized data must be impossible

The vault already has the full rotation/re-encryption machinery: `TokenVaultService.RotateActiveKeyAsync` (atomic key-flip + `cardvault.vault.rotate` outbox audit), `ReEncryptBatchAsync` (re-encrypts entries where `KeyId != active`, emits `cardvault.reencrypt.batch` audit), `VaultReencryptHostedService` (background batch loop), and `VaultSettingsStore` (singleton `ActiveKeyId` + `LastReencryptStatus`). SEC-01 **reuses this workflow verbatim** — it introduces zero new crypto code. The key insight from `ReEncryptBatchAsync`: it selects candidates by `x.KeyId != active`, so **"done" is provably `COUNT(TokenVault WHERE KeyId != activeKeyId) == 0`**. That count is the orphan-proof gate.

**Ordered operational runbook (SHALL be executed in this order):**

1. **Generate a new key `k3`** (32-byte AES-256-GCM, `RandomNumberGenerator`). Add it to the running config as an *available* key alongside `k1`/`k2`, but do **not** yet make it active. `VaultCrypto` loads all keys in `Vault:Keys`; `k1`/`k2` must remain loadable so existing ciphertext stays decryptable.
2. **Rotate active key to `k3`** via `RotateActiveKeyAsync("k3", actor, traceId)`. This flips `VaultSettings.ActiveKeyId` and emits the `cardvault.vault.rotate` audit event atomically. New tokenizations now use `k3`; old records still carry `k1`/`k2`.
3. **Re-encrypt all records** — drive `ReEncryptBatchAsync` (via the existing admin endpoint or by letting `VaultReencryptHostedService` run) until every batch returns `updated == 0`.
4. **Verify** — the gate is `COUNT(TokenVault WHERE KeyId NOT IN ('k3')) == 0`. Note the terminal (zero-remaining) batch reports `LastReencryptStatus == "noop"` and emits **no** `cardvault.reencrypt.batch` audit row — by design, `ReEncryptBatchAsync` only sets `"completed"` and emits an audit event on a batch that migrated at least one record. So confirm progress via the `"completed"` events of the earlier non-empty batches, and confirm completion via the COUNT gate reaching 0 (equivalently, the last batch returning `updated == 0` / `"noop"`). Do NOT wait for `"completed"` to persist at the terminal state — it never will. **This is the gate: revocation MUST NOT proceed until the COUNT reaches 0.**
5. **Revoke `k1`/`k2`** — remove them from `Vault:Keys` config and restart. After restart, `VaultCrypto` can no longer construct those keys; any lingering `k1`/`k2` ciphertext would now throw `Unknown KeyId` on decrypt — which is exactly why step 4 is a hard gate. Revocation = key material physically absent from config + rotated at the source (treated as compromised regardless).
6. **Rotate the non-vault secrets** (Postgres password, SQL Server Identity password, `Seed:AdminPassword`, `OpenBankingClientSecret`, IsoSwitch `Admin:ApiKey`, JWT signing keys) at their sources and update the env-var providers. These have no data-migration dependency — they rotate independently.
7. **Scrub git history** — see below. Coordinated as a discrete announced step, AFTER rotation, because rotation (not scrub) is the real remediation.

**Why this makes orphaning impossible:** revocation (step 5) is strictly gated behind the `KeyId != active` count reaching zero (step 4). The re-encryption is idempotent and resumable (batch loop), and its completion is durably audited via the outbox. If a restart interrupts step 3, the count-based gate simply isn't satisfied and revocation is blocked. There is no code path where `k1`/`k2` disappear while a record still references them.

### Decision: git-history scrub coordination

**Chosen:** `git filter-repo` (BFG-class rewrite) run as a **one-time, announced, out-of-band operation** — NOT automated in a PR. Coordinated steps: (a) freeze merges to `main`, (b) rotate all secrets first (steps 1–6 above make the leaked values worthless before the scrub even runs), (c) run `git filter-repo` to strip the literal secret strings from all history, (d) force-push, (e) all collaborators re-clone. **Tradeoff / one-way door flag:** history rewrite breaks every open fork/branch/CI ref and invalidates existing clones — this is irreversible and disruptive. Mitigation: because rotation already neutralized the secrets, the scrub is defense-in-depth, not the primary control; if the scrub must be deferred for coordination reasons, the security posture still holds on rotation alone. The scrub is documented in the runbook, executed by a human operator, and is explicitly **out of the automated PR diff**.

### Persistence / config shape
- No schema change. `VaultSettings`, `TokenVault`, `RefreshTokens` unchanged.
- `appsettings.Development.json` (both services): remove `Vault:Keys:k1/k2`, connection-string passwords, `Seed:AdminEmail/AdminPassword`, `Seed:OpenBankingClientSecret`, IsoSwitch `Admin:ApiKey`. Keep structural keys (`ActiveKeyId`, non-secret `Vault:Keys` shape as empty, `VaultJob`, ports).
- `backend/.env.example` (new, committed): documents `Vault__Keys__k3`, `ConnectionStrings__Postgres`, `ConnectionStrings__SqlServerIdentity`, `Seed__AdminEmail`, `Seed__AdminPassword`, `Seed__OpenBankingClientSecret`, `Admin__ApiKey` (IsoSwitch), `Jwt__SigningKey`. All placeholder values are the allowlisted `__REPLACE_ME__` token so SEC-06's scanner ignores them.

### Rollback
One-way door on the crypto/rotation side (old keys are treated as compromised; a code revert restores config wiring but operators keep rotated keys). Re-encryption has a full audit trail via the existing vault events.

---

## SEC-02 — Salted, cost-parameterized PIN hashing (Argon2id interim)

### Decision: verify-then-upgrade (rehash on next successful entry), NOT forced reset

**Chosen:** On `VerifyPinAsync`, first detect the stored algorithm. If the record is legacy unsalted-SHA-256, verify against the SHA-256 path; on a **successful** verify, transparently re-hash the same PIN with Argon2id and overwrite `PinHash` (+ new algo/params/salt columns) in the same `SaveChangesAsync`. The old hash is destroyed the moment the upgrade lands. New PINs (`SetPinAsync`) always write Argon2id.

**Rejected — forced PIN reset for all existing cards:** operationally hostile (every cardholder must re-enrol a PIN, cross-channel), and there is no reset delivery flow in scope (password recovery is explicitly out of scope). **Tradeoff:** verify-then-upgrade means a card that is *never used again* keeps its legacy hash indefinitely. Mitigation: the legacy path is verify-only and salt-less hashes are never *written* again, so exposure is bounded to dormant cards; a later batch job (Phase 1, alongside HSM) can force-expire any card still on the legacy scheme. **Rationale:** zero cardholder disruption, no card ever verifiable *only* by the old scheme once touched, and the migration is self-healing under normal traffic. This satisfies the proposal's "no card left verifiable only by the old scheme" once used, and the dormant-card residue is a documented, bounded, forward-fixable gap.

### Decision: Argon2id library and cost parameters

**Chosen library:** `Konscious.Security.Cryptography.Argon2` (`Konscious.Security.Cryptography.Argon2id`) — the de-facto managed .NET Argon2id implementation, no native dependency, works cleanly on the Linux CI runner and the Docker images.
**Rejected — `libsodium`/`NSec`:** native-lib packaging friction in the multi-stage Dockerfiles for marginal gain; `Isopoh.Cryptography.Argon2` is an alternative but `Konscious` is more widely used and simpler. **Rejected — BCrypt/PBKDF2:** Argon2id is the proposal's explicit choice (memory-hard, resists GPU/ASIC), and PBKDF2's only edge (FIPS) is irrelevant here since the definitive control is HSM (Phase 1).

**Default cost params (tuned to the auth-path latency budget):** `memory = 19456 KiB (19 MiB)`, `iterations = 2`, `parallelism = 1`, `salt = 16 bytes` (`RandomNumberGenerator`), `hash length = 32 bytes`. This is the OWASP-recommended Argon2id "second" profile, targeting ~50–100 ms per verify on a typical server core — well inside an interactive auth budget and cheap enough that the sequential PIN-retry lockout (`MaxPinRetries=3`) isn't a DoS lever. Params are **stored per-hash**, so they can be raised later without a migration or a breaking change.

### Decision: persistence shape (algo id + params + salt) + EF migration

Add to `CardEntity` (and the domain `Card` if mirrored):
- `PinHashAlgorithm` — `string?`, `MaxLength(32)`, e.g. `"argon2id"` or legacy `"sha256"` (null = never set).
- `PinHashParams` — `string?`, `MaxLength(128)`, compact JSON: `{"m":19456,"t":2,"p":1}` (only meaningful for argon2id).
- `PinSalt` — `string?`, `MaxLength(64)`, Base64 of the 16-byte salt.
- `PinHash` — existing `string?`, `MaxLength(128)`; keep, but widen tolerance is unnecessary (Base64 of 32-byte Argon2id hash fits in 128).

**PinService redesign:** replace the single `HashPin(pin)` with `HashPinArgon2id(pin, salt, params) → hash` and a `VerifyLegacySha256(pin, storedHash)` helper. `VerifyPinAsync` branches on `card.PinHashAlgorithm`. Never log PIN material (audit events already carry only `cardId`, keep it that way).

**EF migration gotcha (CRITICAL):** `CardVaultDbContext` uses `Database.EnsureCreated()` in Development and `Database.Migrate()` in Production (see `Program.cs` lines 360–369). `EnsureCreated()` **does not run migrations** — it creates the schema from the current model in one shot and is incompatible with a later `Migrate()` on the same DB. The new columns will appear automatically for fresh Development DBs (EnsureCreated reads the updated model), but a **real EF migration** (`dotnet ef migrations add AddPinKdfColumns`) is REQUIRED for the Production `Migrate()` path and for any pre-existing Development DB that was created before this change. The migration adds three nullable columns — additive, no data backfill, no downtime. Nullable defaults mean existing rows read as "legacy/unset" and flow through the verify-then-upgrade path naturally.

### Rollback
**One-way door (flagged):** a code revert reintroduces the vulnerable unsalted path. Once shipped, treat as forward-only; the new columns are harmless if left in place.

---

## SEC-03 — JWT in HttpOnly cookies + security headers (backend + frontend)

This is the single cross-surface slice. Ship backend + Angular in one PR so the token model never straddles two schemes across a merge boundary.

### Decision: cookie attributes + CORS AllowCredentials interaction

**Chosen cookie attributes:**
- **Access token cookie** (`cv_at`) and **refresh token cookie** (`cv_rt`): `HttpOnly=true`, `Secure=true`, `SameSite`.
- `SameSite=Lax` in **Development** (SPA on `http://localhost:4200`, API on `http://localhost:<port>` — same site, different port; Lax works for the top-level SPA and XHR because same-site is host+scheme-registrable-domain, and localhost is treated as same-site). Actually the dev SPA→API is **cross-origin same-site** (both `localhost`), so `SameSite=Lax` + `withCredentials` works and avoids needing `None`.
- `SameSite=Strict` is too aggressive (breaks any top-level nav-driven auth); **`SameSite=Lax`** is the chosen production value when SPA and API share a registrable domain in prod (recommended deployment). If prod ever splits SPA and API onto *different* registrable domains, that requires `SameSite=None; Secure` + a CORS `AllowCredentials` origin allowlist — flagged as a deployment-topology decision, defaulted to same-site + `Lax`.
- Refresh cookie is additionally scoped with `Path=/api/auth` so it is only sent to refresh/logout endpoints, shrinking its attack surface.

**CORS interaction (already wired):** `Program.cs` already builds CORS from `Cors:AllowedOrigins` with `.AllowCredentials()` and no `AllowAnyOrigin` (ADR-4). This is exactly what `withCredentials` cookies require — credentialed CORS **forbids** wildcard origins, and the codebase already uses an explicit allowlist. Dev origin `http://localhost:4200` must be present in `appsettings.Development.json`'s `Cors:AllowedOrigins`. **No CORS code change needed** — only confirm the dev origin is listed. `Secure=true` cookies over `http://localhost` are accepted by browsers (localhost is a secure context exception), so dev stays HTTP-friendly without weakening the Production `Secure` flag.

### Decision: where cookies are written (layering)

**Chosen:** cookie writing lives in the **presentation layer** (`AuthController` / a thin auth-cookie helper), NOT in the MediatR handlers. The handlers (`LoginCommandHandler`, `MfaVerifyCommandHandler`, `RefreshTokenCommandHandler`) currently return `AuthSessionResponse` with `accessToken`/`refreshToken` in the body — they stay pure (no `HttpResponse` dependency, respecting clean-architecture: Application layer must not know about HTTP transport). The controller receives the `AuthSessionResponse`, sets `cv_at`/`cv_rt` cookies from it via `HttpContext.Response.Cookies.Append`, and **strips the raw tokens from the JSON body** (returns only `mfaRequired`, `message`, `user`). Logout is a new `POST /api/auth/logout` endpoint that clears both cookies (`Response.Cookies.Delete`) and revokes the stored refresh token.

**Cookie → auth pipeline acceptance:** the JWT bearer handler reads from the `Authorization` header by default. Add a JWT-bearer `OnMessageReceived` event that, when the header is absent, pulls the access token from the `cv_at` cookie into `context.Token`. This makes the existing `AddJwtBearer` validation work unchanged — token validation params, policies, everything downstream is untouched.

**Refresh model:** `POST /api/auth/refresh` reads the refresh token from the `cv_rt` cookie (no longer from the body), runs the existing rotation handler, and writes fresh cookies. The `RefreshRequest` body becomes optional/empty.

### Decision: security headers

Add a small response-header middleware (or `app.Use(...)` after `UseCors`) emitting on all CardVault responses:
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Content-Security-Policy: default-src 'self'; frame-ancestors 'none'; ...` (tuned to the SPA's needs; `frame-ancestors 'none'` is the CSP-native equivalent of `X-Frame-Options: DENY`).

Placed as middleware so it applies uniformly; Swagger UI in Development may need a relaxed CSP — gate the strict CSP to non-Development or add the Swagger origins to `script-src`/`style-src` in Development only.

### Decision: Angular half (dual-accept cutover?)

**Chosen: NO dual-accept window — atomic switch in one PR.** Because the slice ships backend + frontend together and stacked-to-main, there is no window where a new frontend talks to an old backend or vice versa. This is simpler and avoids leaving the legacy header path alive (which would keep the `localStorage` vulnerability reachable). **Rejected — dual-accept (backend accepts both cookie and header during a window):** only valuable if frontend and backend deploy independently; here they don't, and keeping the header path alive contradicts the whole point of the slice. **Tradeoff:** atomic switch means the deploy must land both halves together — acceptable because it's one PR.

**Angular changes (`auth.service.ts` + `auth.interceptor.ts`):**
- Remove `ACCESS_TOKEN_KEY`/`REFRESH_TOKEN_KEY` `localStorage` reads/writes entirely. `applySessionResponse` no longer stores tokens (they arrive as cookies the browser holds automatically); it stores only the `user` object (or drops `USER_KEY` too and relies on `/auth/me`).
- `auth.interceptor.ts`: drop `attachBearerToken` / the `Authorization` header. Set `withCredentials: true` on API requests (`req.clone({ withCredentials: true })` for `isApiRequest` URLs) so cookies ride along. The 401→refresh retry logic stays, but `refreshSession()` no longer sends a body token — it POSTs to `/auth/refresh` with `withCredentials` and lets the cookie carry the refresh token.
- `isTokenExpired` / client-side JWT parsing is **removed** — the token is `HttpOnly` and unreadable by JS. Session validity is determined by calling `/auth/me` (401 ⇒ not authenticated) or by the refresh flow. `ensureAuthenticated` reworks to: try `/auth/me`; on 401 try refresh then `/auth/me`; else redirect to login.
- `getAccessToken()`/`getRefreshToken()` are removed or return null; the `constructor` guard that calls `clearSession` when no token exists must change to not depend on reading a token from storage.
- Follow the loaded angular-core skill: keep signals, `inject()`, no lifecycle hooks; these are service-level RxJS flows which the skill permits for complex async.

### Rollback
If a cutover toggle is desired, a config flag on the backend could re-enable body tokens + header acceptance, but the chosen design is atomic revert-both-halves-together.

---

## SEC-04 — TLS enforced on the ISO 8583 TCP channel

### Decision: default flip + Production fail-fast with loopback detection

**Chosen:**
1. Flip `TcpIsoClientOptions.UseTls` default from `false` to `true` (one-line change in `TcpIsoClientOptions.cs`).
2. Add a startup validation in IsoSwitch `Program.cs` (in the existing `AddSingleton(sp => ...)` factory that binds `IsoClient`, or a dedicated `IValidateOptions`/startup assertion mirroring the `TokenizationOptionsValidator` fail-fast pattern) that throws `InvalidOperationException` at startup when: `!env.IsDevelopment()` (i.e. Production) **AND** `opt.UseTls == false` **AND** the configured `opt.Host` is **not** a loopback address. Plaintext stays permitted for loopback simulators in any environment.

**Loopback detection:** resolve `opt.Host` and treat as loopback if it is `"localhost"`, or parses to an `IPAddress` where `IPAddress.IsLoopback(addr)` is true (covers `127.0.0.0/8` and `::1`), or DNS-resolves exclusively to loopback addresses. Use `IPAddress.TryParse` first (fast path for literal `127.0.0.1`/`::1`), fall back to `Dns.GetHostAddresses` for hostnames, and if resolution fails, **fail closed** (treat as non-loopback → require TLS). `AllowInvalidCert` stays gated to Development (already enforced at `Program.cs` line 140, ADR-7) — no change.

**Rejected — enforce TLS everywhere including loopback:** breaks the in-process/dev `IsoSimulatorServer` which speaks plaintext; the proposal explicitly keeps loopback plaintext. **Rejected — silent default without fail-fast:** a plaintext prod deploy could slip through if an operator flips `UseTls=false`; the fail-fast is the actual control. **Tradeoff:** DNS resolution at startup adds a small dependency, mitigated by literal-IP fast path + fail-closed on resolution error.

### Rollback
Cleanly reversible — revert the default; operational config can still force TLS on.

---

## SEC-05 — Remove default admin seed + hardcoded admin API key

### Decision (CardVault): Development-only seed, remove `??` fallbacks

**Chosen:** In `Program.cs`, wrap the admin/seed-user creation block so it runs **only when `app.Environment.IsDevelopment()`** — same gate already used for the catalog seed below it. Remove the `?? "admin@demo.com"` and `?? "Admin1234!"` fallbacks (and `?? "OpenBanking123!"`): read `Seed:AdminEmail` / `Seed:AdminPassword` from config; if Development and they are missing, fail loudly (or skip seeding with a warning) — never silently fabricate a known admin. In non-Development, **no auto-seed at all**; admin provisioning becomes a deliberate operator action (out of scope to build a provisioning tool here — the requirement is only the *removal* of auto-seed).

**Layering note:** the role-seeding (`Admin`/`Operator`/`Auditor` roles) can stay unconditional (roles are not secrets), but **user** seeding with the shared password moves inside the Development gate.

### Decision (IsoSwitch): operator-supplied admin API key, reject `dev-admin-key`, fail-fast

**Chosen:** Mirror `TokenizationOptionsValidator` exactly. The `Admin:ApiKey` config is currently **read by nothing in code** (grep confirms it exists only in `appsettings.Development.json`) — so SEC-05 must (a) introduce an `AdminApiKeyOptions` bound to the `Admin` section with `ValidateOnStart()`, and (b) add an `AdminApiKeyOptionsValidator : IValidateOptions<AdminApiKeyOptions>` that fails startup when the key is missing, shorter than a minimum length, or equals/contains the forbidden placeholder set — **adding `"dev-admin-key"` to the `Forbidden` array** alongside `DEV_ONLY`/`CHANGE_ME`/`placeholder`. Strip `Admin:ApiKey` from `appsettings.Development.json` (operator supplies via `Admin__ApiKey` env var).

**Gotcha:** since nothing currently consumes the key, this slice establishes the validation contract; the actual *authentication* middleware that checks incoming requests against the key is either already elsewhere or a follow-on — the SEC-05 requirement is the **fail-fast validation + placeholder rejection**, consistent with how SEC-1/2/3 (Tokenization/Jwt) validate secrets they consume. Spec should state the validation requirement; if a consumer must be added, it is a small addition in the same slice.

### Rollback
Reverting restores the seed fallback (undesirable) — prefer forward fix. Low runtime risk.

---

## SEC-06 — Secret scanning in CI + pre-commit

### Decision: gitleaks (both CI and pre-commit)

**Chosen: gitleaks**, used identically in CI and locally.
**Rejected — TruffleHog:** strong verifier (live-credential checks) but heavier, slower, and its config ergonomics are worse for a monorepo allowlist. gitleaks is single-binary, fast, has a mature `gitleaks.toml` allowlist model, a first-party GitHub Action (`gitleaks/gitleaks-action`), and a first-party pre-commit hook — one tool, two placements, one config file. **Tradeoff:** gitleaks is regex/entropy-based (no live verification), so it can false-positive; mitigated by the allowlist below. For a "stop re-committing secrets" gate, detection-not-verification is the right and cheaper choice.

### CI placement (`.github/workflows/ci.yml`)

Add a **new independent job** `secret-scan` (parallel to `build-test`, no `needs:`) so it gates the PR without waiting on the build:
```yaml
  secret-scan:
    name: Secret Scan (gitleaks)
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }   # full history so PRs scan the whole diff range
      - uses: gitleaks/gitleaks-action@v2
        env: { GITLEAKS_CONFIG: .gitleaks.toml }
```
`fetch-depth: 0` is required so gitleaks scans the commit range, not just the tip. Job fails the run on any finding (default gitleaks behavior).

### Pre-commit mechanism

**Chosen:** the `pre-commit` framework (`.pre-commit-config.yaml`) with the official `gitleaks` hook (`repo: https://github.com/gitleaks/gitleaks`, `rev: <pinned>`, `id: gitleaks`). It runs on `git commit` against staged changes, catching a secret before it enters history. **Rejected — a hand-rolled `.git/hooks/pre-commit` shell script:** not versioned, not shared, drifts per-developer. `.pre-commit-config.yaml` is committed and reproducible; developers run `pre-commit install` once. **Tradeoff:** requires developers to install the framework (documented in `.env.example` / README); the CI job is the enforcing backstop for anyone who skips the local hook.

### Baseline / allowlist (`.gitleaks.toml`)

Allowlist the intentional non-secret placeholders so the pipeline doesn't false-positive on SEC-01's `.env.example`:
- Path allowlist: `backend/.env.example` and the appsettings skeletons.
- Regex allowlist for the `__REPLACE_ME__` placeholder token and other obvious dummies.
- **Do NOT** allowlist by commit SHA for the old leaked keys — those must remain detectable so a re-add is caught; history is handled by SEC-01's scrub, not by an allowlist that would blind the scanner.

### Rollback
The job/hook can be disabled independently; introduces no runtime behavior. Land first so the gate exists for the rest of the phase.

---

## Cross-cutting: one-way doors (flagged for reviewers)

1. **SEC-01 git-history rewrite** — irreversible, breaks all clones/forks; run out-of-band by a human after rotation, not in a PR.
2. **SEC-01 key revocation** — once `k1`/`k2` leave config and are treated as compromised, there is no going back; strictly gated behind the zero-orphan re-encryption count.
3. **SEC-02 KDF change** — reverting reintroduces the vulnerable unsalted path; treat as forward-only once merged.
4. **SEC-03 SameSite=None deployment topology** — if prod ever splits SPA/API onto different registrable domains, that is a separate deployment decision (`None; Secure` + credentialed CORS allowlist); the default is same-site `Lax`.

## Assumptions requiring validation
- Prod deploys SPA and API under the same registrable domain (drives `SameSite=Lax`). If not, revisit SEC-03 cookie `SameSite`.
- The IsoSwitch `Admin:ApiKey` has (or will have) a consumer; SEC-05 delivers the fail-fast validation contract regardless, mirroring existing secret validators.
- `Konscious.Security.Cryptography.Argon2` builds cleanly in the existing Linux Docker multi-stage images (managed, no native dep — expected fine).
- `EnsureCreated()` vs `Migrate()` divergence is understood: SEC-02 ships a real EF migration for the Production/`Migrate()` path; fresh Dev DBs pick up columns via `EnsureCreated` reading the updated model.
