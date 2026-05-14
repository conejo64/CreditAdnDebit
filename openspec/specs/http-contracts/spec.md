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
