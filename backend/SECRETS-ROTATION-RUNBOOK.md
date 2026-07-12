# Secrets Rotation Runbook (SEC-01)

## 1. Purpose

This runbook closes the SEC-01 PCI audit finding: committed development config
(`appsettings.Development.json` for CardVault and IsoSwitch) previously carried live
secret material — vault encryption keys `k1`/`k2`, inline connection-string passwords,
a shared admin seed password, and IsoSwitch's `dev-admin-key` admin API key literal.

The remediation is **rotation first, history scrub second**: rotating credentials is the
real fix (an old committed secret becomes worthless once revoked), and scrubbing git
history is defense-in-depth on top of that, not a substitute for it.

**Every step in this document is executed by a human operator, out-of-band.** None of
these steps are automated in CI, and none are executed as part of the SEC-01 code/test
PR. The PR only ships the code (config purge, `.env.example` template, fail-fast
validation, and the automated tests that prove the vault workflow behaves correctly
during and after this procedure).

---

## 2. Ordered Procedure

Follow this order strictly. Do not skip ahead — each step depends on the previous one
completing successfully.

### Step 1 — Generate the new vault key `k3`

Generate a 32-byte AES-256-GCM key with a cryptographically secure RNG, Base64-encoded:

```bash
# .NET (any machine with the SDK installed)
dotnet run --project ./tools/keygen -- --bytes 32
# or, without a helper project:
openssl rand -base64 32
```

Provision `Vault__Keys__k3` via the environment/secrets-manager for every environment
that needs it (see `backend/deploy/.env.example` for the full list of required
variables). Do NOT commit the value anywhere. `k1`/`k2` stay loaded alongside `k3`
during the transition window — `VaultCrypto` requires every key referenced by an
existing `TokenVault` row to remain loadable until re-encryption completes.

**Verification:** `GET /health/vault` shows `k3` as an available key; `activeKeyId` is
still the pre-rotation key at this point.

### Step 2 — Rotate the active key to `k3`

Call the existing admin endpoint (see
`backend/services/CardVault/docs/runbooks/vault-key-rotation.md` Section 4 for full
HTTP mechanics, rate-limit override, and troubleshooting):

```http
POST /api/vault/rotate-active-key?keyId=k3
Authorization: Bearer <admin-jwt>
```

This flips `VaultSettings.ActiveKeyId` to `k3` and emits exactly one
`cardvault.vault.rotate` audit event. New tokenizations after this point encrypt under
`k3`; pre-existing records remain under `k1`/`k2` until re-encrypted.

Automated coverage: `TokenVaultServiceRotateAuditTests`,
`TokenVaultServiceRotateColdStartAtomicityTests`.

### Step 3 — Re-encrypt all existing records to `k3`

Run the re-encryption batch loop to completion (manually via the admin endpoint, or let
the `VaultReencryptHostedService` schedule handle it — see vault-key-rotation.md
Section 5):

```http
POST /api/vault/reencrypt?take=200
Authorization: Bearer <admin-jwt>
```

Repeat until the batch reports `updatedCount = 0`.

Automated coverage: `TokenVaultServiceReencryptAuditTests`,
`VaultRevocationGateTests.OrphanProofGate_ZeroCount_OpensAfterFullReencryption`.

### Step 4 — Verify the orphan-proof gate reaches COUNT == 0

**This is the one-way-door check before revocation. Do not proceed on `"completed"`
status alone.**

The correct gate condition is:

```sql
SELECT COUNT(*) FROM token_vault WHERE key_id NOT IN ('k3');
-- must return 0
```

`VaultSettings.LastReencryptStatus` is a convenience field, not the gate itself:
- `"completed"` — the last batch migrated at least one record.
- `"noop"` — the last batch had nothing left to migrate, **and this is the expected
  terminal state**, not a failure or a stall. A terminal no-op batch also emits **no**
  `cardvault.reencrypt.batch` audit event, by design (see the "Spec clarification
  follow-up" item in `tasks.md` — the base spec is being clarified to state this
  explicitly).

Only the `COUNT(...) == 0` query above is authoritative for "is it safe to revoke".

Automated coverage: `VaultRevocationGateTests.OrphanProofGate_NonZeroCount_BlocksRevocation`,
`VaultRevocationGateTests.TerminalBatch_ZeroRemainingRecords_ReportsNoopStatus_NotCompleted`,
`VaultRevocationGateTests.TerminalBatch_ZeroRemainingRecords_EmitsNoAuditEvent`,
`VaultRevocationGateTests.TerminalBatch_AfterRealBatch_SecondCallIsNoopAndGateStaysOpen`.

### Step 5 — Revoke `k1`/`k2`

Only after Step 4's COUNT gate confirms `0`:

1. Remove `k1` and `k2` from every environment's `Vault:Keys` configuration — only `k3`
   remains.
2. Remove the commented-out transition-window `Vault__Keys__k1` /
   `Vault__Keys__k2` lines from `.env.example` templates (they are commented out by
   default in `backend/deploy/.env.example`; delete them entirely once revoked).
3. Restart CardVault.Api so the running process picks up the reduced key set.

**Consequence of revoking too early (before Step 4's gate is zero):** any `TokenVault`
row still referencing `k1`/`k2` becomes permanently undecryptable by the running
service — `VaultCrypto.DecryptFromParts` throws `Unknown KeyId` for those rows. This is
intentional fail-loud behavior, not a bug — it is exactly why the gate exists.

Automated coverage: `VaultRevocationGateTests.Revocation_WhileGateNonzero_DecryptStillWorksForRemainingOldKeyRecords`
(proves the failure mode a premature revocation produces),
`VaultRevocationGateTests.RevokedKey_DecryptAttempt_ThrowsUnknownKeyId`,
`VaultRevocationGateTests.RevokedKey_DecryptAttempt_ExceptionContainsNoPlaintextPan`.

### Step 6 — Rotate the non-vault secrets

Independent of the vault key rotation (no data-migration dependency) — rotate these at
their respective sources and update the environment/secrets-manager providers:

| Secret | Source system | Notes |
|---|---|---|
| `ConnectionStrings__Postgres` (CardVault) | Postgres instance | Change the `postgres` role password; update the connection string. |
| `ConnectionStrings__SqlServerIdentity` | SQL Server instance | Change the `sa` (or dedicated identity-service login) password. |
| `Seed__AdminPassword` | CardVault identity store | Only relevant for the Development-gated seed (SEC-05) — rotate the value used locally; never set in Production. |
| `Seed__OpenBankingClientSecret` | Open Banking client registration | Rotate at the Open Banking provider/registration, then update the env var. |
| `ConnectionStrings__Postgres` (IsoSwitch) | Postgres instance | Separate database (`isoswitch`), same Postgres instance — rotate the role password there too if it differs from CardVault's. |
| `Admin__ApiKey` (IsoSwitch) | Operator-generated | Must not be the literal `dev-admin-key` — generate a new operator-supplied value; enforced by `AdminApiKeyOptionsValidator` fail-fast (SEC-05 / PR 6). |
| `Jwt__SigningKey` (both services) | Operator-generated, ≥32 chars | Rotating this invalidates all currently issued tokens — coordinate a maintenance window; all active sessions will need to re-authenticate. |

### Step 7 — Freeze merges to `main`

Before starting the git-history scrub (Step 8), freeze merges to `main` for the
duration of the scrub + force-push + re-clone window. This prevents new commits from
being based on history that is about to be rewritten.

### Step 8 — Scrub git history

**This step happens AFTER rotation (Steps 1–6), never before.** Rotation is the real
remediation — a leaked-but-revoked key is no longer a live secret. The history scrub is
defense-in-depth so the old (now-revoked) values are not sitting in the repository's
history in the clear, but it does not substitute for actually rotating the credentials.

```bash
# git-filter-repo (preferred over the legacy filter-branch / BFG for path+content rules)
git filter-repo --replace-text backend/secrets-redaction-rules.txt
```

Where `backend/secrets-redaction-rules.txt` lists the exact leaked values to replace
(the two `k1`/`k2` Base64 key values, the inline connection-string passwords, the seed
admin password, the `dev-admin-key` literal) — build this file at execution time from
the pre-rotation values; do not commit it with real values in it.

- Coordinate this as a single, announced maintenance operation.
- After the scrub, force-push the rewritten history:
  ```bash
  git push --force-with-lease origin main
  ```
- **All collaborators must discard their local clones and re-clone** — a rewritten
  history is not compatible with `git pull`/rebase against old local copies.

### Step 9 — Re-clone and verify

Every collaborator re-clones the repository fresh. Confirm:
- `git log -p -- '**/appsettings.Development.json'` no longer shows the old `k1`/`k2`
  values, inline passwords, or the `dev-admin-key` literal at any point in history.
- CI (`secret-scan` job, SEC-06) passes on the rewritten `main`.
- The application starts successfully with the new secrets provisioned via
  `backend/deploy/.env.example`.

---

## 3. Summary Checklist (operator-executed, human-in-the-loop)

- [ ] Step 1 — Generate `k3`, provision via env/secrets-manager (NOT committed)
- [ ] Step 2 — Rotate active key to `k3` (`POST /api/vault/rotate-active-key`)
- [ ] Step 3 — Run re-encryption batches to completion
- [ ] Step 4 — Verify `COUNT(TokenVault WHERE KeyId NOT IN ('k3')) == 0`
- [ ] Step 5 — Revoke `k1`/`k2` from all environment configs; restart CardVault.Api
- [ ] Step 6 — Rotate Postgres/SQL Server/seed/OpenBanking/IsoSwitch admin key/JWT secrets
- [ ] Step 7 — Freeze merges to `main`
- [ ] Step 8 — Run `git filter-repo`, force-push, announce the rewrite
- [ ] Step 9 — All collaborators re-clone; verify CI and application startup

## 4. Related documents

- `backend/services/CardVault/docs/runbooks/vault-key-rotation.md` — detailed HTTP-level
  rotation/re-encryption mechanics, rate-limit override, and troubleshooting table.
- `backend/deploy/.env.example` — the full list of required secret-bearing environment
  variables for CardVault, IsoSwitch, and IsoAudit.
- `.gitleaks.toml` — CI/pre-commit secret-scanning config (SEC-06); the allowlist for
  `backend/deploy/.env.example` depends on this file's placeholder values never
  becoming real secrets.
