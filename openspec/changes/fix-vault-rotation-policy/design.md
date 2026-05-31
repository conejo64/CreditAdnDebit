# Design: Fix Vault Rotation Rate-Limit Policy

## Technical Approach

Register the missing `vault_admin_ops` rate-limit policy in `Program.cs` next to `vault_detokenize`, bind its limits from `Vault:AdminRateLimit:*` config, and route rotation/re-encryption audit through the **transactional EF outbox** instead of the current fire-and-forget `IEventBus.PublishAsync`. Implements the `vault-and-pci` delta scenarios (rate window, 429 burst, audit emission, outbox durability, startup policy assertion). No refactor of the rotation pipeline, hosted scheduler, or auth policy.

## Architecture Decisions

| Decision | Choice | Alternatives rejected | Rationale |
|----------|--------|-----------------------|-----------|
| Policy registration | `AddPolicy("vault_admin_ops", ...)` fixed-window, alongside `vault_detokenize` (Program.cs:165) | Dedicated `VaultAdminRateLimiter` class | Two endpoints share one policy; indirection not yet justified (per proposal). Mirrors existing pattern. |
| Partition key | `httpContext.User?.Identity?.Name ?? RemoteIpAddress ?? "anon"` | Per-IP only; global limiter | Throttle the actor, not the host; matches `vault_detokenize`. `[Authorize]` rejects anon upstream so IP is degraded-auth fallback only. |
| Limit source | Bind `VaultOptions.AdminRateLimit` from `Vault:AdminRateLimit:*`; prod default 5/300s/queue 0, dev relaxed | Hard-coded limits | Banking ops must retune per rotation window without redeploy. Queue 0 surfaces denials fast. |
| Audit transport | `_db.OutboxMessages.Add(...)` committed in the SAME `SaveChangesAsync` as the state change | Keep direct `IEventBus.PublishAsync` (current) | Spec requires audit transactional with rotation; current direct publish is fire-and-forget and lost on Kafka outage. Outbox relayed by `EfOutboxPublisher`. |
| Startup assertion | Resolve `RateLimiterOptions` at boot; throw descriptive `InvalidOperationException` if `vault_detokenize` or `vault_admin_ops` absent | Rely on first-request 500 | Fail fast before serving traffic; prevents silent regression recurrence. |

> NOTE — proposal correction: `AdminRateLimit` options go in `VaultCrypto.cs` (where `VaultOptions` is actually defined, line 83), not a non-existent `VaultOptions.cs`. Audit is NOT absent today — it exists via direct `IEventBus`/`PciAuditPublisher` but is non-transactional; this change MOVES it onto the outbox and aligns the payload shape.

## Data Flow

    POST /api/vault/rotate-active-key
      │ [Authorize CanRotateVaultKeys] ── fail ─→ 403 (no audit)
      │ [EnableRateLimiting vault_admin_ops] ── over limit ─→ 429 (no audit)
      ▼
    RotateActiveKeyCommandHandler ─→ TokenVaultService.RotateActiveKeyAsync
      ├─ SetActiveKeyId + OutboxMessages.Add(VaultKeyRotated)
      └─ ONE SaveChangesAsync (state + audit row, atomic)
                                   │
                          EfOutboxPublisher ─→ Kafka sw.cardvault.audit

Re-encryption follows the same shape emitting `VaultReencryptionBatchCompleted` with `recordsAffected`.

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `Program.cs` | Modify | Add `AddPolicy("vault_admin_ops", ...)`; bind limits from `Vault:AdminRateLimit`; add startup assertion both policies registered + INFO log of bound limits. |
| `Vault/VaultCrypto.cs` (`VaultOptions`) | Modify | Add `AdminRateLimit` nested options (`PermitLimit`, `WindowSeconds`, `QueueLimit`) with code defaults. |
| `Vault/TokenVaultService.cs` | Modify | In `RotateActiveKeyAsync` / `ReEncryptBatchAsync` replace direct `_bus.PublishAsync` audit with `_db.OutboxMessages.Add` committed in the same `SaveChangesAsync`; align payload to spec field names. |
| `appsettings.json` | Modify | Default `Vault:AdminRateLimit` (relaxed dev). |
| `appsettings.Production.json` | Modify | Tightened prod values (5/300/0). |
| `tests/CardVault.Tests/Features/Vault/` | Create | Integration + startup regression tests. |
| `docs/runbooks/vault-key-rotation.md` | Create | Operator runbook (detailed in tasks phase). |
| `openspec/specs/vault-and-pci/spec.md` | Modify | Apply delta scenarios. |

## Interfaces / Contracts

Outbox audit payloads (topic `sw.cardvault.audit`, no key material / PAN):

```jsonc
// VaultKeyRotated
{ "type": "cardvault.vault.rotate", "actor": "...", "keyId": "k2",
  "traceId": "...", "rotatedAt": "2026-05-29T...Z" }
// VaultReencryptionBatchCompleted
{ "type": "cardvault.reencrypt.batch", "actor": "...",
  "traceId": "...", "recordsAffected": 17, "completedAt": "...Z" }
```

Config keys: `Vault:AdminRateLimit:PermitLimit | WindowSeconds | QueueLimit`.

## Testing Strategy

Test seam: the existing `CardVaultWebApplicationFactory` suppresses hosted services and swaps `IEventBus` for `NullEventBus`, so audit assertions read the **outbox rows in the InMemory `CardVaultDbContext`** (the durable path is what we observe). Use a fake `TimeProvider` for the limiter to avoid timing flakiness.

| Layer | What to Test | Approach |
|-------|-------------|----------|
| Unit | Startup regression: both `vault_detokenize` + `vault_admin_ops` registered | Resolve `RateLimiterOptions`/boot host; assert policies present, descriptive throw if missing |
| Integration | 200 authorized rotate/reencrypt under normal load | `GenerateJwt(["Admin"])`; assert 200 + `VaultKeyRotated` outbox row, no key material |
| Integration | 429 on burst | Fire > `PermitLimit` requests in window; assert 429 and NO new outbox audit row |
| Integration | 403 unauthorized | JWT without `CanRotateVaultKeys`; assert 403, no state change, no audit |
| Integration | Re-encrypt audit | Assert `VaultReencryptionBatchCompleted` row with `recordsAffected` |

## Migration / Rollout

No data migration. Config-only defaults shipped in `appsettings.*`. Rollback = revert the policy registration + audit-transport change; endpoints return to current broken state with no working flow regressed.

## Open Questions

- [ ] Re-encryption runs both via endpoint and `VaultReencryptHostedService` (actor `system-job`) — confirm scheduler-triggered batches should ALSO emit the audit event (design assumes yes, since emission lives in `TokenVaultService`).
- [ ] Keep `PciAuditPublisher.PublishAsync` call alongside the outbox row, or consolidate to outbox only to avoid double audit? Design keeps PCI publisher; flag for review.
