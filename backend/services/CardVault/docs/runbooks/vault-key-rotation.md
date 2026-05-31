# Vault Key Rotation Runbook

## 1. Purpose

CardVault stores encrypted PANs under AES-256-GCM with key identifiers managed via `Vault:Keys` configuration. Periodic key rotation is required by:
- **PCI-DSS 3.6.4** — cryptographic key management procedures, including key change at defined crypto-periods.
- **SB Resolución JB-2014-3066** (Junta Bancaria Ecuador) — information security controls for payment data environments.

Rotation involves two steps:
1. Change the **active key ID** (new tokens encrypt under the new key; old tokens remain encrypted under the old key until re-encrypted).
2. **Re-encrypt** existing vault entries to the new key in batches.

---

## 2. Preconditions

Before starting:

- [ ] The new key ID (e.g., `k2`) is provisioned in `Vault:Keys` in the deployment environment's configuration.  Keys must be 32 bytes AES-256, base64-encoded.
- [ ] Confirm the current `vault_admin_ops` rate-limit config: `Vault:AdminRateLimit:PermitLimit` / `WindowSeconds`. If the window is tight, apply a pre-rotation override (see Section 3).
- [ ] Confirm the `EfOutboxPublisher` hosted service is running (check `/health` and application logs for outbox relay activity).
- [ ] Confirm Kafka topic `sw.cardvault.audit` is reachable and the consumer group is healthy (or is acceptable to buffer in the outbox during connectivity gaps).

---

## 3. Pre-Rotation Rate-Limit Override

The `vault_admin_ops` rate-limit policy (default prod: 5 req / 300 s) may be too tight for a controlled rotation window where multiple attempts might be needed.

**Override via environment variable (no redeploy required):**

```bash
# Override for current deployment (Docker / Kubernetes)
VAULT__ADMINRATELIMIT__PERMITLIMIT=20
VAULT__ADMINRATELIMIT__WINDOWSECONDS=300
```

ASP.NET Core's configuration reads `VAULT__ADMINRATELIMIT__PERMITLIMIT` as `Vault:AdminRateLimit:PermitLimit` (double-underscore → colon mapping).

**Revert** this override after rotation completes.

---

## 4. Rotation Procedure

### 4.1 Rotate the Active Key

```http
POST /api/vault/rotate-active-key?keyId=k2
Authorization: Bearer <admin-jwt>
```

**Required role / permission:** `Admin` role OR `vault:rotate-keys` permission claim.

**Expected response (`200 OK`):**
```json
{
  "activeKeyId": "k2",
  "rotatedOn": "2026-05-29T15:30:00.000Z",
  "actor": "admin@example.com"
}
```

**On `403 Forbidden`:** The caller lacks `CanRotateVaultKeys`. Obtain an Admin JWT.  
**On `429 Too Many Requests`:** The rate-limit window is exhausted. Wait for the window to reset or apply the override from Section 3.  
**On `400 Bad Request`:** The `keyId` value is not present in `Vault:Keys`. Provision the key first.

### 4.2 Verify the Rotation

```http
GET /health/vault
Authorization: Bearer <admin-jwt>
```

Expected: `activeKeyId` equals the new key ID.

---

## 5. Re-Encryption Procedure

After rotation, old vault entries still encrypted under the previous key must be migrated. Re-encryption runs in batches via the `POST /api/vault/reencrypt?take={n}` endpoint. The `VaultReencryptHostedService` also runs this automatically on a schedule (see `VaultJob` config).

### 5.1 Trigger a Manual Batch

```http
POST /api/vault/reencrypt?take=200
Authorization: Bearer <admin-jwt>
```

**Expected response (`200 OK`):**
```json
{
  "activeKeyId": "k2",
  "updatedCount": 142,
  "rotatedOn": "2026-05-29T15:32:00.000Z"
}
```

Repeat until `updatedCount` returns `0` (no remaining entries under the old key).

### 5.2 Monitor Progress

```http
GET /health/vault
Authorization: Bearer <audit-jwt>
```

Fields to observe:
- `lastReencryptUpdated` — number of records migrated in the last batch.
- `lastReencryptStatus` — `"completed"` when records were migrated, `"noop"` when no remaining entries.
- `lastReencryptRunOn` — timestamp of the last run.

### 5.3 Verify Outbox Audit Rows

Query the `OutboxMessages` table (or the `sw.cardvault.audit` Kafka topic) for re-encryption completion events:

```sql
SELECT payload_json, occurred_on
FROM outbox_messages
WHERE topic = 'sw.cardvault.audit'
  AND payload_json LIKE '%cardvault.reencrypt.batch%'
ORDER BY occurred_on DESC
LIMIT 10;
```

Confirm:
- `type` = `"cardvault.reencrypt.batch"`
- `recordsAffected` matches expectations.
- No key material, PAN-like strings, or `nonceB64`/`cipherB64` fields in the payload.

---

## 6. Audit Verification

After rotation + re-encryption, verify the audit trail:

### 6.1 Rotation audit row
```sql
SELECT payload_json FROM outbox_messages
WHERE topic = 'sw.cardvault.audit'
  AND payload_json LIKE '%cardvault.vault.rotate%'
ORDER BY occurred_on DESC
LIMIT 1;
```

Expected payload fields: `type`, `actor`, `keyId`, `traceId`, `rotatedAt` (UTC). Must NOT contain key bytes or PAN.

### 6.2 Re-encryption audit rows (one per batch)
```sql
SELECT payload_json FROM outbox_messages
WHERE topic = 'sw.cardvault.audit'
  AND payload_json LIKE '%cardvault.reencrypt.batch%'
ORDER BY occurred_on;
```

Sum of `recordsAffected` across all batches must equal the total vault entries that were under the old key.

---

## 7. Rollback

If the rotation must be reverted:

1. **Revert the active key config** — set `Vault:ActiveKeyId` back to the old key ID in configuration and redeploy (or call `POST /api/vault/rotate-active-key?keyId=k1` if the old key is still in `Vault:Keys`).
2. **Re-encrypt back** — run `POST /api/vault/reencrypt?take=500` until `updatedCount=0` to migrate entries re-encrypted under the new key back to the old key.
3. **Confirm** via `GET /health/vault` that `activeKeyId` reverts and `lastReencryptStatus=noop`.

> **Important:** Do NOT remove the old key from `Vault:Keys` until all vault entries are fully migrated and verified. Removing a key while entries still reference it will make those entries unreadable.

---

## 8. Troubleshooting

| Symptom | Cause | Resolution |
|---------|-------|------------|
| `429 Too Many Requests` on rotation | `vault_admin_ops` PermitLimit exhausted | Wait for the window to reset, or apply the env-var override from Section 3. |
| `403 Forbidden` | Caller does not have `CanRotateVaultKeys` | Re-authenticate with an Admin account or add `vault:rotate-keys` perm claim. |
| Audit row missing after rotation | `EfOutboxPublisher` not running or Kafka unreachable | Check hosted service logs; outbox row is durable — it will be delivered when connectivity is restored. |
| `InvalidOperationException` on startup: "vault_admin_ops is not registered" | `Program.cs` missing the `AddPolicy("vault_admin_ops")` call | Check `Program.cs` rate-limiter registration block; ensure the policy is added. |
| `updatedCount` never reaches 0 | New entries created under the old key after rotation | Confirm `activeKeyId` changed successfully and the `VaultStartupInitializer` applied it at startup. |
| Base64-like string in audit payload | Bug in payload serialization | Check `TokenVaultService.RotateActiveKeyAsync` — only `keyId` (opaque string) should appear, not the key bytes. |
