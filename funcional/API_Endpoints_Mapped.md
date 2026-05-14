# Endpoints Mapped in CardVault.Api v1

This document lists all the endpoints exposed in `CardVault.Api/Program.cs` and matches them to a potential UI Menu/Screen module.

## 1. Authentication & Security
These endpoints don't directly map to a menu, they represent the Auth flow (Login screen) and user registration.

*   `POST /api/auth/register`
*   `POST /api/auth/login`
*   `POST /api/auth/mfa/enable`
*   `POST /api/auth/mfa/verify`
*   `POST /api/auth/refresh`

## 2. Infrastructure & Vault (Bóveda Segura)
Functions meant for secure tokenization of PANs and vault maintenance.

*   `POST /api/tokens/tokenize` 
*   `POST /api/tokens/detokenize` (Requires special role/policy)
*   `GET /api/tokens/{token}/metadata`
*   `GET /api/vault/active-key`
*   `POST /api/vault/rotate-active-key`
*   `POST /api/vault/reencrypt`
*   `GET /api/health/vault`

## 3. Operations & Audit
Modules tracking system health, activity, and external messaging queues.

*   `GET /api/audit/latest`
*   `GET /api/outbox/latest`
*   `GET /api/switch/journal`
*   `POST /api/demo/publish`

## 4. Catalogs & Switch Configuration
Dictionaries and routing configuration.

*   `GET /api/routing-rules`
*   `POST /api/routing-rules`
*   `GET /api/catalog/countries`
*   `POST /api/catalog/countries`
*   `GET /api/catalog/bins`
*   `POST /api/catalog/bins`
*   `GET /api/catalog/card-products`
*   `POST /api/catalog/card-products`

## 5. Issuer - Customers & Accounts
Managing the base CRM elements.

*   `POST /api/issuer/customers`
*   `GET /api/issuer/customers`
*   `GET /api/issuer/customers/{id}`
*   `POST /api/issuer/accounts`

## 6. Issuer - Cards lifecycle
Card operations attached to accounts.

*   `POST /api/issuer/cards/issue`
*   `GET /api/issuer/cards/{id}`
*   `POST /api/issuer/cards/{id}/activate`
*   `POST /api/issuer/cards/{id}/block`

## 7. Financial Ledgers
Raw financial entries representing purchases, payments, and fees.

*   `POST /api/ledger/purchase`
*   `POST /api/ledger/payment`
*   `POST /api/ledger/fee`
*   `POST /api/ledger/interest`
*   `GET /api/ledger/accounts/{accountId}/balance`
*   `GET /api/issuer/accounts/{id}/available-credit`
*   `PUT /api/credit/policies`
*   `GET /api/credit/policies/{productCode}`

## 8. Billing & Statements
Group of ledger entries converted into monthly statements and fee application.

*   `POST /api/billing/statements/generate`
*   `GET /api/billing/statements/{id}`
*   `GET /api/billing/accounts/{accountId}/statements`
*   `POST /api/billing/statements/{id}/pay`
*   `GET /api/billing/statements/{id}/buckets`
*   `POST /api/billing/statements/{id}/recalculate`
*   `GET /api/billing/statements/{id}/print`
*   `GET /api/billing/statements/{id}/pdf`
*   `POST /api/billing/statements/{id}/latefee`
*   `POST /api/billing/latefees/run`
*   `POST /api/fees/overlimit`
*   `POST /api/fees/annual`
*   `POST /api/fees/cash-advance`
*   `POST /api/interest/accrue`
*   `GET /api/interest/accruals`
*   `GET /api/billing/policies/minimum-payment`
*   `POST /api/billing/policies/minimum-payment`

## 9. Settlements & Reconciliations
Interacting with the external Network layer to consolidate clearing batches.

*   `POST /api/settlement/run`
*   `GET /api/settlement/batches`
*   `GET /api/settlement/batches/{id}`
*   `GET /api/reconciliation/settlement/{batchId}`

## 10. Fraud & Disputes
Case tracking for card claims or dynamic fraud prevention rules.

*   `GET /api/disputes/accounts/{accountId}`
*   `POST /api/disputes/{id}/close`
*   `POST /api/disputes/{id}/transition`
*   `GET /api/disputes/{id}/events`
*   `GET /api/risk/mcc-rules`
*   `POST /api/risk/mcc-rules`
*   `DELETE /api/risk/mcc-rules/{mcc}`
*   `GET /api/risk/velocity-rules`
*   `POST /api/risk/velocity-rules`
*   `DELETE /api/risk/velocity-rules/{id}`
*   `POST /api/holds/expire/run`

---

# Endpoints Mapped in IsoSwitch.Api v1

This list contains all the endpoints mapped from `IsoSwitch.Api/Program.cs`. 

## 11. Switch Simulator & System
Administrative controls for the simulator environments that could have a dedicated dashboard section.

*   `GET /api/simulator/options`
*   `GET /api/tcp/status`
*   `POST /api/demo/pan-map`
*   `GET /api/demo/pan-map`
*   `GET /api/demo/iso0100`

## 12. Switch Routing & Configuration (Admin)
Management of rules mapping BINs and Network to specific outbound connectors.

*   `GET /api/routing/cache`
*   `POST /api/routing/decision`
*   `GET /api/routing/rules/v2`
*   `POST /api/routing/rules/v2`
*   `DELETE /api/routing/rules/v2/{id}`

## 13. Synchronized Catalogs (Cache)
Local fast-cache representations for global vault catalogs. 

*   `GET /api/catalog/cache/bins`
*   `GET /api/catalog/cache/countries`
*   `GET /api/catalog/cache/card-products`
*   `GET /api/catalog/currencies`
*   `POST /api/catalog/currencies`
*   `GET /api/catalog/networks`
*   `POST /api/catalog/networks`
*   `GET /api/catalog/participants`
*   `POST /api/catalog/participants`

## 14. Network Logs, Audit & Journals
Raw transaction histories passing through the switch and detailed binary logs.

*   `GET /api/transactions`
*   `GET /api/transactions/{traceId}`
*   `GET /api/iso/logs/{traceId}`
*   `GET /api/audit/latest`

## 15. Operational Commands & Transactions (Internal/Testing)
Core endpoints for authorizing and capturing real ISO messages, and triggering maintenance checks. Unlikely to be directly triggered from UI except for testing forms.

*   `POST /api/iso/authorize`
*   `POST /api/iso/capture`
*   `POST /api/iso/reversal`
*   `POST /api/iso/reversal-advice`
*   `POST /api/iso/reconcile`
*   `POST /api/iso/network/ping`
*   `POST /api/iso/network/signon`
*   `POST /api/iso/network/signoff`
*   `POST /api/simulate/purchase/approve`
*   `POST /api/simulate/purchase/reverse`
*   `POST /api/simulate/refund`
*   `POST /api/simulate/chargeback`
*   `POST /api/simulate/auth/approve`
*   `POST /api/simulate/auth/reverse`
*   `POST /api/simulate/clearing`
