# Proposal: Fix Vault Rotation Rate-Limit Policy

## Intent

`TokensController` exposes two PCI-sensitive admin endpoints — `POST /api/vault/rotate-active-key` (line 70) and `POST /api/vault/reencrypt` (line 80) — both annotated with `[EnableRateLimiting("vault_admin_ops")]`. The referenced policy is **never registered** in `Program.cs` (only `vault_detokenize` is registered at line 165). Calling either endpoint in production therefore throws `InvalidOperationException` at request time, blocking periodic key rotation.

Note: The user-supplied context referred to `VaultController`; the actual file is `Controllers/TokensController.cs`. Same endpoints, same defect.

This is a live regulatory blocker:

- **PCI-DSS 3.6.4** requires cryptographic key changes at the end of the defined cryptoperiod. With these endpoints broken, CardVault cannot perform a documented rotation.
- **Superintendencia de Bancos del Ecuador, Resolución JB-2014-3066** (operational risk for banking technology) requires controlled cryptographic operations with audit trail. The current code path neither rate-limits nor explicitly audits rotation/re-encryption.
- The hardening rebaseline already shipped JWT + RBAC across CardVault/IsoSwitch; vault key rotation is the remaining gap in the `vault-and-pci` capability before external PCI assessment.

Success means: an Admin can rotate the active key in production, the operation is throttled against accidental flooding, every rotation emits a PCI-safe audit event, and the test suite covers both endpoints (success, rate-limit, audit emission).

## Scope

### In Scope

- Register the `vault_admin_ops` rate-limit policy in `Program.cs` next to `vault_detokenize`, partitioned by authenticated actor (fallback to remote IP), with environment-configurable limits.
- Bind limits from configuration (`Vault:AdminRateLimit:PermitLimit`, `Vault:AdminRateLimit:WindowSeconds`, `Vault:AdminRateLimit:QueueLimit`) with safe production defaults (suggested: 5 requests / 5 minutes / queue 0) and a relaxed development default for local testing.
- Add a PCI-safe audit event on successful rotation and on each re-encryption batch run — emitted via the existing `IEventBus` / outbox path used by other sensitive vault operations, with `actor`, `keyId` (identifier only — never key material), `traceId`, `recordsAffected` (re-encrypt only), and UTC timestamp.
- Add operator runbook `backend/services/CardVault/docs/runbooks/vault-key-rotation.md` covering: precondition checks, rotation call, re-encryption monitoring, rollback, and audit verification.
- Add focused integration tests under `backend/services/CardVault/tests/CardVault.Tests/Features/Vault/` covering: 200 under normal load, 429 under burst, audit event emitted, unauthorized caller rejected, and a startup regression test asserting the policy is registered.

### Out of Scope

- HSM-backed key custody and KEK/DEK split — owned by the `hsm-integration` change (Phase 2). This proposal keeps the existing software-managed key store.
- Frontend UI for triggering rotation — operators continue to use the existing admin tooling / direct API call.
- Reworking the `VaultReencryptHostedService` background scheduler or its batching algorithm.
- Changing the `CanRotateVaultKeys` authorization policy or the `vault:rotate-keys` permission catalogue entry.
- Cross-service propagation of rotation events to IsoSwitch (no shared key material today).

## Capabilities

### Modified Capabilities

- `vault-and-pci`: tighten the existing **Vault Key Rotation** and **PCI-Safe Audit Events** requirements with explicit rate-limit and audit-emission scenarios so the spec matches enforced behaviour.

### New Capabilities

- None.

## Approach

Smallest correct fix first: register the missing policy and add the missing audit event — do not refactor the rotation pipeline. Rationale:

1. **Register-don't-rewrite**: the `[EnableRateLimiting]` attributes and the controller surface are already correct. The defect is a single missing `AddPolicy` call. Adding it restores the documented contract without touching the handler signature.
2. **Config-driven limits**: hard-coded limits would force a redeploy for every tuning change. Banking ops needs to relax limits during scheduled rotations and tighten during normal operation. Bind from `IConfiguration` with conservative defaults.
3. **Partition by actor, fall back to IP**: matches the existing `vault_detokenize` pattern (Program.cs:165) for consistency. Anonymous calls are rejected upstream by `[Authorize]`, so the IP fallback only matters in degraded auth scenarios.
4. **Audit via outbox**: re-use the existing `IEventBus` + `EfOutboxPublisher` pipeline (Program.cs:180-182) so audit events are transactional with the rotation state change — no separate logging path to drift. The current `RotateActiveKeyCommandHandler` (TokenCommands.cs:67-78) does not emit any audit today; this is the natural injection point.
5. **Startup regression test**: a unit test that loads the rate-limiter options and asserts both `vault_detokenize` and `vault_admin_ops` are registered prevents this defect class from recurring silently.

Why not a more invasive option (e.g. introducing a `VaultAdminRateLimiter` service class)? It adds indirection for two endpoints that share one policy. The cost outweighs the readability gain at this stage; revisit if a third admin endpoint is added.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `backend/services/CardVault/src/CardVault.Api/Program.cs` | Modified | Register `vault_admin_ops` policy, bind limits from `Vault:AdminRateLimit:*`. |
| `backend/services/CardVault/src/CardVault.Api/Vault/VaultOptions.cs` | Modified | Add `AdminRateLimit` options section (PermitLimit, WindowSeconds, QueueLimit). |
| `backend/services/CardVault/src/CardVault.Api/Features/Tokens/Commands/TokenCommands.cs` | Modified | `RotateActiveKeyCommandHandler` and re-encryption command path emit `VaultKeyRotated` / `VaultReencryptionBatchCompleted` audit events through the existing event bus. |
| `backend/services/CardVault/src/CardVault.Api/Controllers/TokensController.cs` | Touched only if needed | No behavioural change expected — the attribute is already in place. |
| `backend/services/CardVault/tests/CardVault.Tests/Features/Vault/` | New | Integration + unit tests for rotate-active-key and reencrypt, including 429 path, audit emission, and startup policy-registration regression. |
| `backend/services/CardVault/docs/runbooks/vault-key-rotation.md` | New | Operator runbook. |
| `backend/services/CardVault/src/CardVault.Api/appsettings.json` | Modified | Add default `Vault:AdminRateLimit` section. |
| `backend/services/CardVault/src/CardVault.Api/appsettings.Production.json` | Modified | Tightened production defaults. |
| `openspec/specs/vault-and-pci/spec.md` | Modified | Adjusted scenarios under **Vault Key Rotation** and **PCI-Safe Audit Events**. |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Production rotation window blocked by overly aggressive default limits. | Medium | Make limits configurable; document the recommended pre-rotation override in the runbook; default queue limit 0 so denials surface fast rather than hang. |
| Audit event addition creates duplicate events if both handler and service emit. | Low | Centralise audit emission in the command handler only; document the contract in the spec. |
| Test flakiness around the fixed-window rate limiter (timing-sensitive). | Medium | Use a fake `TimeProvider` (already available in .NET 9) in tests; avoid `Thread.Sleep`. |
| Configuration drift between environments leaves prod with dev limits. | Medium | Add startup log line emitting the bound limits at INFO; explicit value in `appsettings.Production.json`. |
| `IEventBus` Kafka outage swallows audit on rotation. | Low | Rely on existing outbox publisher (`EfOutboxPublisher`) so the audit row is persisted transactionally with the rotation and retried by the publisher. |

## Rollback Plan

- **Code**: revert the `AddPolicy("vault_admin_ops", ...)` registration and the audit-event emission. Endpoints return to the current broken-but-known state; rotations stop, but no data is at risk because the rotation never succeeded.
- **Configuration**: remove the `Vault:AdminRateLimit` section from `appsettings.*.json`. Defaults in code are safe.
- **Tests**: remove the new `Features/Vault/` test folder.
- **Runbook**: delete `docs/runbooks/vault-key-rotation.md`.
- **Spec**: revert the scenario additions under `vault-and-pci`.

Because the change only enables an endpoint pair that does not currently work, rollback cannot regress any working flow.

## Dependencies

- Existing `IEventBus` + `EfOutboxPublisher` pipeline (`Program.cs:180-182`) — already in production.
- Existing `CanRotateVaultKeys` policy and `vault:rotate-keys` permission (`PermissionCatalog.cs:27`) — already in production.
- .NET 9 `Microsoft.AspNetCore.RateLimiting` (already referenced for `vault_detokenize`).
- No dependency on `hsm-integration` (Phase 2) — this change is independent and ships first.

## Success Criteria

- [ ] `POST /api/vault/rotate-active-key` returns `200 OK` for an authorized Admin under normal load.
- [ ] `POST /api/vault/reencrypt` returns `200 OK` for an authorized Admin under normal load.
- [ ] Both endpoints return `429 Too Many Requests` when invocation rate exceeds the configured window.
- [ ] A `VaultKeyRotated` audit event is published on every successful rotation, containing `actor`, `keyId`, `traceId`, and UTC timestamp — never key material or PAN.
- [ ] A `VaultReencryptionBatchCompleted` audit event is published for each re-encrypt batch run, containing `actor`, `traceId`, `recordsAffected`, and UTC timestamp.
- [ ] A startup regression test fails if either `vault_detokenize` or `vault_admin_ops` is missing from the rate-limiter registry.
- [ ] Unauthorized callers receive `403 Forbidden` (existing `[Authorize(Policy = "CanRotateVaultKeys")]` still enforced).
- [ ] Operator runbook published under `backend/services/CardVault/docs/runbooks/`.
- [ ] `vault-and-pci` spec updated with explicit rate-limit and audit scenarios.
