# Tabla de Compatibilidad HTTP — Frontend / Backend

**Generado:** 2026-05-03  
**Fase:** Semana 3 del Plan de Estabilización  
**Alcance:** Módulos críticos auditados contra sus controllers y endpoints reales

---

## Convenciones

- `apiUrl` = `http://localhost:5101/api` (CardVault.Api)
- `isoSwitchUrl` = `http://localhost:5201/api` (IsoSwitch.Api)
- ✅ Contrato alineado
- 🔴 Bug corregido en esta sesión
- ⚠️ Observación menor (no bloqueante)

---

## Auth

| Método frontend | Endpoint backend | Estado | Notas |
|----------------|-----------------|--------|-------|
| `AuthService.login()` | `POST api/auth/login` | ✅ | Devuelve `accessToken`, `refreshToken`, `mfaRequired`, usuario |
| `AuthService.refresh()` | `POST api/auth/refresh` | ✅ | Rotación de token, hash SHA-256 |
| `AuthService.me()` | `GET api/auth/me` | ✅ | `[Authorize]` requerido |
| `AuthService.logout()` | limpia localStorage | ✅ | Sin endpoint dedicado (stateless JWT) |

---

## Customers / Accounts / Cards (IssuerController)

| Método frontend | Endpoint backend | Estado | Notas |
|----------------|-----------------|--------|-------|
| `CustomerService.getCustomers()` | `GET api/issuer/customers?q=&take=` | ✅ | |
| `CustomerService.getCustomer(id)` | `GET api/issuer/customers/{id}` | ✅ | |
| `CustomerService.createCustomer()` | `POST api/issuer/customers` | ✅ | |
| `CustomerService.updateCustomer()` | `PUT api/issuer/customers/{id}` | ✅ | |
| `CardService.getCards()` | `GET api/issuer/cards?q=&take=` | ✅ | |
| `CardService.issueCard()` | `POST api/issuer/cards` | ✅ | |
| `CardService.blockCard()` | `POST api/issuer/cards/{id}/block` | ✅ | |
| `CardService.unblockCard()` | `POST api/issuer/cards/{id}/unblock` | ✅ | |

---

## Finance — Ledger

| Método frontend | Endpoint backend | Estado | Notas |
|----------------|-----------------|--------|-------|
| `FinanceService.getAccountLedger()` | `GET api/ledger/accounts/{id}/movements` | 🔴 | Endpoint creado en esta sesión |
| `FinanceService.getStatements()` | `GET api/billing/accounts/{id}/statements?take=` | 🔴 | Agregado `take=24`; backend ya tenía fallback `take<=0 → 20` |
| `FinanceService.payStatement()` | `POST api/billing/statements/{id}/pay` | ✅ | |
| `FinanceService.simulatePurchase()` | `POST api/ledger/purchase` | ✅ | |

---

## Finance — Settlement

| Método frontend | Endpoint backend | Estado | Notas |
|----------------|-----------------|--------|-------|
| `SettlementService.getBatches()` | `GET api/settlement/batches?take=` | ✅ | `take=0` → backend devuelve 50 defensivamente |
| `SettlementService.getBatchDetails()` | `GET api/settlement/batches/{id}` | ✅ | |
| `SettlementService.runSettlement(network, date)` | `POST api/settlement/run?network=&businessDate=` | 🔴 | Servicio y componente actualizados; agregados inputs de red y fecha |
| `SettlementService.getReconciliation()` | `GET api/reconciliation/settlement/{batchId}` | ✅ | |

---

## Security — Antifraud

| Método frontend | Endpoint backend | Estado | Notas |
|----------------|-----------------|--------|-------|
| `AntifraudService.getRules()` | `GET api/risk/rules` | 🔴 | URL duplicaba `/api/api/risk/rules`; corregido |
| `AntifraudService.createRule()` | `POST api/risk/rules` | ✅ | |
| `AntifraudService.updateRule()` | `PUT api/risk/rules/{id}` | ✅ | |
| `AntifraudService.deleteRule()` | `DELETE api/risk/rules/{id}` | ✅ | |

---

## Security — Disputes

| Método frontend | Endpoint backend | Estado | Notas |
|----------------|-----------------|--------|-------|
| `DisputeService.getDisputesByAccount()` | `GET api/disputes/accounts/{id}?take=` | ✅ | `take=0` → backend devuelve 50 defensivamente |
| `DisputeService.getDisputeEvents()` | `GET api/disputes/{id}/events?take=` | ✅ | `take=0` → backend devuelve 50 defensivamente |
| `DisputeService.transitionDispute()` | `POST api/disputes/{id}/transition` | ✅ | |
| `DisputeService.closeDispute(id, won)` | `POST api/disputes/{id}/close?won=` | 🔴 | Agregado parámetro `won`; antes siempre cerraba como perdida |

---

## Security — Vault

| Método frontend | Endpoint backend | Estado | Notas |
|----------------|-----------------|--------|-------|
| `VaultService.getActiveKey()` | `GET api/vault/active-key` | 🔴 | Respuesta extendida con `availableKeyIds`; componente ahora rota a la siguiente llave disponible |
| `VaultService.rotateKey(keyId)` | `POST api/vault/rotate-active-key?keyId=` | 🔴 | Faltaba `keyId`; corregido en servicio y componente |
| `VaultService.reEncrypt(take)` | `POST api/vault/reencrypt?take=` | ✅ | |
| `VaultService.getAuditLogs(limit)` | `GET api/audit/latest?take=` | ✅ | |

---

## Switch Monitor (SwitchService → IsoSwitch.Api)

| Método frontend | Endpoint backend | Estado | Notas |
|----------------|-----------------|--------|-------|
| `SwitchService.getAuditLogs()` | `GET api/audit/latest` (CardVault) | ✅ | `take` ausente → backend devuelve 50 defensivamente |
| `SwitchService.getTransactions()` | `GET api/transactions` (IsoSwitch) | ✅ | Parámetros opcionales: `status`, `connectorId`, `take`, `q` |
| `SwitchService.simulateAuthorize()` | `POST api/iso/authorize` (IsoSwitch) | ✅ | |
| `SwitchService.simulateReversal()` | `POST api/iso/reversal` (IsoSwitch) | ✅ | |
| `SwitchService.simulateCapture()` | `POST api/iso/capture` (IsoSwitch) | ✅ | |

---

## Módulos no críticos (auditados sin cambios)

| Módulo | Servicio | Estado |
|--------|---------|--------|
| Analytics | `analytics.service.ts` → `api/analytics/*` | ✅ |
| Accounting | `accounting.service.ts` → `api/accounting/*` | ✅ |
| Credit Limit | `credit-limit.service.ts` → `api/credit-limits/*` | ✅ |
| Installments | `installment.service.ts` → `api/installments/*` | ✅ |
| Open Banking | `open-banking.service.ts` → `api/openbanking/*` | ✅ |
| Notifications | `notifications.service.ts` → `api/notifications/*` | ✅ |
| Wallets | `wallets.service.ts` → `api/wallets/*` | ✅ |
| Loyalty | `loyalty.service.ts` → `api/loyalty/*` | ✅ |
| Catalog (IsoSwitch) | `catalog.service.ts` → `api/catalog/*` | ✅ |
| Admin | `admin.service.ts` → `api/admin/*` | ✅ |

---

## Resumen de correcciones aplicadas

| Fix | Archivo(s) | Descripción |
|-----|-----------|-------------|
| Fix 1 | `antifraud.service.ts` | URL doble `/api/api/` → `/api/` |
| Fix 2 | `LedgerController.cs`, `LedgerService.cs`, `LedgerQueries.cs` | Nuevo endpoint `GET /ledger/accounts/{id}/movements` |
| Fix 3 | `finance.service.ts` | `getStatements` ahora envía `take=24` |
| Fix 4 | `settlement.service.ts`, `settlement-list.component.ts` | `runSettlement(network, date)` + inputs en UI |
| Fix 5 | `TokenQueries.cs`, `vault.service.ts`, `vault.component.ts` | Rotación con `keyId` correcto; `GET /vault/active-key` expone `availableKeyIds` |
| Fix 6 | `dispute.service.ts` | `closeDispute(id, won)` pasa el parámetro `won` |

---

## Requirement HC-1: Installment Service Route MUST Resolve Without Duplicated Segment

The `InstallmentService` base URL SHALL be constructed as `${environment.apiUrl}/billing` (not `/api/billing`), because `environment.apiUrl` already contains the `/api` suffix.

### Invariant
- The effective URL for any installment request MUST NOT contain the substring `/api/api/`.
- `getPlans(accountId)` MUST issue a `GET` request to exactly `<apiUrl>/billing/installment-plans?accountId={accountId}` (or the documented sub-path), with zero doubled segments.
- `deferPurchase(payload)` MUST issue a `POST` request to exactly `<apiUrl>/billing/installment-plans`.

### Scenario HC-1-S1: Plans list resolves without 404
- GIVEN `environment.apiUrl` is `http://localhost:5101/api`
- AND `InstallmentService.baseUrl` is set to `${environment.apiUrl}/billing`
- WHEN the frontend calls `getPlans(accountId)`
- THEN the outgoing HTTP request URL is `http://localhost:5101/api/billing/installment-plans?accountId={accountId}` (no `/api/api/`)
- AND the backend returns HTTP 200

### Scenario HC-1-S2: Existing `/api/api/billing/` double-segment is gone
- GIVEN the previous broken configuration where `baseUrl` contained `/api/billing` appended to an `apiUrl` already ending in `/api`
- WHEN the fix is applied
- THEN no request in the installments feature ever reaches the path `/api/api/billing/...`
- AND integration smoke tests assert this URL shape constraint

---

## Requirement HC-2: Five New Endpoint Contracts MUST Be Documented

The following five endpoints SHALL be added to the HTTP contract table. Each entry establishes the canonical method, path, required authorization policy, request body schema, and success response.

> **Implementation note:** The base `http-contracts` spec currently lists `CardService.unblockCard()` → `POST api/issuer/cards/{id}/unblock` as ✅, which contradicts the inline verification that the backend endpoint does NOT exist. Before implementing HC-2.1, RE-VERIFY `IssuerController.cs` for an existing `unblock` route. If it already exists, the gap is only frontend wiring + tests, not a new endpoint.

### HC-2.1 — `POST /api/issuer/cards/{id}/unblock`

| Field | Value |
|---|---|
| Method | POST |
| Path | `/api/issuer/cards/{id}/unblock` |
| Auth Policy | `CanOperateIssuer` |
| Request Body | none (id in path) |
| Success Response | 204 No Content |
| Error Responses | 401 Unauthorized, 403 Forbidden, 404 Not Found |
| Domain Event | `CardUnblockedEvent` |

### HC-2.2 — `POST /api/issuer/cards/{id}/cancel`

| Field | Value |
|---|---|
| Method | POST |
| Path | `/api/issuer/cards/{id}/cancel` |
| Auth Policy | `CanOperateIssuer` |
| Request Body | `{ "reason": "string" }` (optional) |
| Success Response | 204 No Content |
| Error Responses | 401 Unauthorized, 403 Forbidden, 404 Not Found |
| Domain Event | `CardCancelledEvent` |

### HC-2.3 — `POST /api/issuer/cards/{id}/replace`

| Field | Value |
|---|---|
| Method | POST |
| Path | `/api/issuer/cards/{id}/replace` |
| Auth Policy | `CanOperateIssuer` |
| Request Body | `{ "reason": "string" }` (optional) |
| Success Response | 201 Created — body: `{ "newCardId": "uuid" }` |
| Error Responses | 401 Unauthorized, 403 Forbidden, 404 Not Found, 409 Conflict (card already cancelled) |
| Domain Event | `CardReplacedEvent` |

### HC-2.4 — `POST /api/auth/forgot-password`

| Field | Value |
|---|---|
| Method | POST |
| Path | `/api/auth/forgot-password` |
| Auth Policy | `[AllowAnonymous]` |
| Rate Limit | Applied (per-IP, configurable threshold) |
| Request Body | `{ "email": "string" }` |
| Success Response | 202 Accepted — body: `{}` (enumeration-safe: same response for known and unknown emails) |
| Error Responses | 400 Bad Request (malformed body), 429 Too Many Requests |

### HC-2.5 — `POST /api/auth/reset-password`

| Field | Value |
|---|---|
| Method | POST |
| Path | `/api/auth/reset-password` |
| Auth Policy | `[AllowAnonymous]` |
| Rate Limit | Applied (per-IP, configurable threshold) |
| Request Body | `{ "token": "string", "newPassword": "string" }` |
| Success Response | 204 No Content |
| Error Responses | 400 Bad Request (expired token / reused token / password policy violation), 429 Too Many Requests |

### Scenario HC-2-S1: Unblock endpoint is reachable and authorized
- GIVEN a caller with the `CanOperateIssuer` policy
- WHEN they POST to `/api/issuer/cards/{id}/unblock`
- THEN the backend returns 204

### Scenario HC-2-S2: Cancel endpoint returns 204 for authorized caller
- GIVEN a caller with the `CanOperateIssuer` policy
- WHEN they POST to `/api/issuer/cards/{id}/cancel`
- THEN the backend returns 204

### Scenario HC-2-S3: Replace endpoint returns 201 with new card id
- GIVEN a caller with the `CanOperateIssuer` policy
- AND the target card is in a blockable/active state
- WHEN they POST to `/api/issuer/cards/{id}/replace`
- THEN the backend returns 201 with a JSON body containing `newCardId`

### Scenario HC-2-S4: Forgot-password returns 202 for known email
- GIVEN a registered email address
- WHEN an anonymous caller POSTs `{ "email": "<registered>" }` to `/api/auth/forgot-password`
- THEN the backend returns 202 Accepted

### Scenario HC-2-S5: Forgot-password returns 202 for unknown email (enumeration-safe)
- GIVEN an email address that does NOT exist in the system
- WHEN an anonymous caller POSTs `{ "email": "<unknown>" }` to `/api/auth/forgot-password`
- THEN the backend returns 202 Accepted
- AND the response body and timing are indistinguishable from the known-email case

### Scenario HC-2-S6: Reset-password returns 204 on valid token
- GIVEN a valid, unexpired, unused reset token
- WHEN an anonymous caller POSTs `{ "token": "<valid>", "newPassword": "<compliant>" }` to `/api/auth/reset-password`
- THEN the backend returns 204

### Scenario HC-2-S7: Reset-password returns 400 on expired or reused token
- GIVEN a token that is expired OR has already been used
- WHEN an anonymous caller POSTs to `/api/auth/reset-password`
- THEN the backend returns 400 Bad Request
