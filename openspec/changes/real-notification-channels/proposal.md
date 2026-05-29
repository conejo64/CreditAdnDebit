# Proposal: Real Notification Channels (SMS, Email)

## Intent

`CardVault.Api.Services.NotificationService.DispatchPendingDeliveriesAsync` (lines 107-109) currently marks every pending delivery as `NotificationDeliveryStatus.Sent` with `ProviderReference = $"sim-{...}"`. No SMS, email, or push reaches the cardholder. This is a placeholder left from `add-customer-notifications`.

The Ecuadorian SBS (Superintendencia de Bancos) and the Junta de Política y Regulación Monetaria require **effective notification** to the cardholder for every credit/debit card transaction. The current simulator will fail any SBS audit and undermines fraud-detection signaling — cardholders cannot react to unauthorized activity if the message never arrives. It also blocks two downstream changes that depend on real outbound delivery:

- `secure-user-registration` (operator invitation emails)
- `fix-frontend-broken-features` (forgot-password emails)

This proposal replaces the simulator with a provider-backed dispatcher that integrates real SMS (Twilio + Movistar Ecuador) and real Email (SendGrid), persists provider responses, retries on failure with bounded attempts, and reconciles asynchronous delivery reports via signed inbound webhooks.

## Scope

### In Scope

- **Provider abstraction**:
  - New `INotificationProvider` interface with `SendAsync(NotificationChannel, recipient, payload, ct)` returning a provider response (status, providerReference, errorCode, errorMessage).
  - New `INotificationProviderRegistry` resolving the correct provider per `(tenantId, channel)` tuple.

- **SMS adapters**:
  - `TwilioSmsProvider` (default international/fallback) — REST API + status callback.
  - `MovistarEcuadorSmsProvider` (preferred for EC mobile prefixes) — SOAP/REST per Movistar B2B contract + DLR callback.

- **Email adapter**:
  - `SendGridEmailProvider` — transactional API with template IDs + `Event Webhook` for delivery/open/bounce.

- **Templating**:
  - Razor-based templates (`.cshtml`) for: OTP, transaction notification, security alert, statement available, payment received. Variables come from the notification payload (PCI-safe — never include raw PAN or OTP secret).
  - Templates support `es-EC` and `en-US` locales; locale selection from `Customer.PreferredLocale`, default `es-EC`.

- **Dispatcher worker rewrite**:
  - `NotificationDispatcherWorker` (background hosted service) replaces in-band dispatch from `NotificationService.DispatchPendingDeliveriesAsync` for real sends.
  - Batches pending deliveries, transitions state `Pending → Sending → Sent | Failed`.
  - On `Failed`, schedules a retry by setting `NextAttemptOn` with exponential backoff (`30s`, `2m`, `10m`) up to `MaxAttempts = 3`. On final failure, transition to `DeadLetter` and emit `cv.notification.deadletter` outbox event.

- **Inbound delivery callbacks**:
  - New endpoint `POST /api/notifications/delivery-callback/{providerId}` (provider id from a closed set: `twilio`, `movistar-ec`, `sendgrid`).
  - Each provider signature is verified using the provider's documented HMAC scheme + secret from secret store. Unsigned or mismatched requests return `401`.
  - Endpoint is allow-listed in auth pipeline (no JWT — provider auth via signature) and rate-limited.
  - Callback updates the matching `CustomerNotificationDeliveryEntity.Status`, `DeliveredOn`, `LastError`, and emits an audit event `cardvault.notification.delivery-confirmed`.

- **Per-tenant configuration**:
  - New `TenantNotificationSettings` table: `(TenantId, Channel, ProviderId, Enabled, Priority)`.
  - Feature flag selector chooses primary provider per tenant; if primary fails after retry budget, the dispatcher tries the secondary (Twilio is the global fallback for SMS).
  - Per-tenant Razor template overrides stored as resource paths.

- **Audit & observability**:
  - PCI audit events: `pci.notification.send-attempt`, `pci.notification.send-result`, `pci.notification.delivery-confirmed`, `pci.notification.deadletter`.
  - Metrics: `notifications_sent_total{channel,provider,status}`, `notifications_retry_total`, `notifications_provider_latency_ms` histogram.
  - Trace propagation: provider call wrapped in a child span; `providerReference` written to baggage for cross-system correlation.

- **Secrets & configuration**:
  - Provider API keys read from environment / vault, never from `appsettings.*.json` committed to git. Local dev uses `dotnet user-secrets`.
  - New configuration section `Notifications:Providers:{Twilio|MovistarEc|SendGrid}` with non-secret fields only (base URL, sender id, template ids).

### Out of Scope

- **Push notifications** for the mobile app — deferred to Fase 4 (no mobile app yet).
- **In-app notifications** rendering and read-state tracking in the Angular frontend — already covered by existing `CustomerNotifications` REST contract; UI work is its own change.
- **Customer preference center / opt-in management** — channel selection per customer is deferred (today the system sends to every channel where the customer has a destination).
- **Marketing or bulk campaigns** — this change is exclusively for transactional and regulatory notifications.
- **WhatsApp Business / RCS / Apple Business Chat** — not required for SBS compliance.
- **Backfill of historical "sim-*" deliveries** — those stay as-is; only deliveries created after the migration use real providers.

## Capabilities

### Modified Capabilities

- **`customer-notifications`** — delta to existing spec. New SHALL requirements:
  - Real provider integration (no simulator path in production).
  - Signed delivery webhooks with HMAC verification.
  - Per-tenant routing with primary/fallback provider chain.
  - Retry with bounded attempts and exponential backoff.
  - Audit events for send attempts and delivery confirmations.
  - PCI-safe templating (no PAN, no OTP secret, only masked metadata).

### New Capabilities

- None. This is a delta on `customer-notifications`.

## Approach

We isolate provider concerns behind `INotificationProvider` so the existing `NotificationService` creation/persistence paths remain unchanged — only the dispatch loop changes. The hosted worker is the right home for the dispatch because:

1. **Resilience**: the worker can survive a single delivery failure without rolling back the originating transaction.
2. **Back-pressure**: a batched worker naturally bounds outbound provider calls and avoids hammering the provider on a transaction storm.
3. **Retry semantics**: bounded attempts with persisted `NextAttemptOn` survive process restarts — an in-band call cannot.

The dispatcher transitions deliveries through explicit states (`Pending → Sending → Sent | Failed → DeadLetter`), persisted on every transition, so a crash mid-send leaves the system in a recoverable state (a `Sending` row older than the lock TTL is reclaimed by the next worker tick).

We chose Twilio + Movistar EC for SMS (rather than a single provider) because:

- **Movistar EC** has direct interconnect with Ecuadorian mobile networks → lower latency and lower per-message cost for `+593` numbers (~80% of our cardholders).
- **Twilio** covers international roaming customers and acts as fallback when Movistar's gateway is unreachable.

SendGrid is the email choice because it provides templated transactional email + a mature Event Webhook for bounce/delivery tracking — both required for SBS evidence trail.

Signed webhooks are mandatory: an unsigned callback endpoint is a forgery vector that would let an attacker mark fraudulent transactions as "delivered" and erase the regulatory audit trail. Each provider has a documented HMAC signature scheme; we verify before touching the database.

> **Sequencing note:** Per the agent's risk assessment, Movistar EC has the highest integration uncertainty (SOAP vs REST B2B contract). Land **Twilio + SendGrid first as one slice**; ship **Movistar EC as a follow-up slice** (per-tenant routing already supports adding it without rework).

> **Architecture path note:** Provider classes are scoped under `CardVault.Api/Services/Notifications/` in this proposal, consistent with the existing flat-Api modular monolith pattern (NOT the empty `CardVault.Application` stub). Confirm against `kill-or-promote-domain-layers` outcome during `sdd-design`.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `backend/services/CardVault/src/CardVault.Api/Services/Notifications/INotificationProvider.cs` | New | Provider abstraction |
| `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Providers/TwilioSmsProvider.cs` | New | Twilio adapter |
| `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Providers/MovistarEcuadorSmsProvider.cs` | New | Movistar EC adapter (slice 2) |
| `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Providers/SendGridEmailProvider.cs` | New | SendGrid adapter |
| `backend/services/CardVault/src/CardVault.Api/Services/Notifications/NotificationProviderRegistry.cs` | New | Per-tenant routing |
| `backend/services/CardVault/src/CardVault.Api/Services/Notifications/NotificationTemplateRenderer.cs` | New | Razor template renderer |
| `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Templates/*.cshtml` | New | OTP, transaction, security alert, statement, payment received (×2 locales) |
| `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/{Twilio,Movistar,SendGrid}SignatureValidator.cs` | New | Per-provider HMAC validation |
| `backend/services/CardVault/src/CardVault.Api/Services/NotificationService.cs` | Modified | Remove simulator branch in `DispatchPendingDeliveriesAsync`; dispatch becomes worker-driven |
| `backend/services/CardVault/src/CardVault.Api/Workers/NotificationDispatcherWorker.cs` | Modified/Rewrite | Real provider invocation, state machine, backoff |
| `backend/services/CardVault/src/CardVault.Api/Controllers/NotificationsController.cs` | Modified | Add `POST /api/notifications/delivery-callback/{providerId}` |
| `backend/services/CardVault/src/CardVault.Infrastructure.Persistence/Notifications/CustomerNotificationDeliveryEntity.cs` | Modified | Add `NextAttemptOn`, `ProviderId`, `TenantId`; extend status enum with `Sending`, `DeadLetter` |
| `backend/services/CardVault/src/CardVault.Infrastructure.Persistence/Notifications/TenantNotificationSettingsEntity.cs` | New | Per-tenant routing table |
| `backend/services/CardVault/src/CardVault.Infrastructure.Persistence/Migrations/*_RealNotificationChannels.cs` | New | EF migration |
| `backend/services/CardVault/src/CardVault.Api/Security/PermissionCatalog.cs` | Modified | Add `notifications:admin` permission (manage tenant settings) |
| `backend/services/CardVault/src/CardVault.Api/Program.cs` | Modified | Register providers, registry, renderer, options, HMAC validators, allow-list webhook endpoint |
| `backend/services/CardVault/src/CardVault.Api/appsettings.json` | Modified | Add `Notifications:Providers` non-secret config skeleton |
| `backend/services/CardVault/tests/CardVault.Tests/Features/Notifications/` | New | Provider adapters (with HTTP mocks), webhook signature validation, state machine, retry/backoff, registry routing |
| `openspec/specs/customer-notifications/spec.md` | Modified | New SHALL requirements (real providers, signed webhooks, retry, per-tenant routing, audit events) |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Provider outage blocks every notification | Medium | Primary/fallback chain per tenant; Twilio acts as global SMS fallback; SendGrid Email is single-provider but has 99.9% SLA — accept residual risk and document. |
| Webhook signature bypass | Low | Mandatory HMAC verification before any DB write; deny by default; integration tests cover unsigned, wrong-signature, and replay scenarios. |
| Retry storm hammers provider after partial outage | Medium | Exponential backoff with jitter; per-provider rate limiter (token bucket); circuit breaker opens after N consecutive failures in a window. |
| Leaking secrets to git | Low | Only non-secret config in `appsettings.json`; secrets via env vars or vault; CI step greps for known key prefixes (`SG.`, `AC[0-9a-f]{32}`). |
| Regulatory audit fails because providerReference is missing | Low | `providerReference` is required to transition `Sending → Sent`; webhook updates persist provider's authoritative reference; PCI audit captures every transition. |
| Movistar EC integration takes longer than Twilio | High | Land Twilio + SendGrid first as a single PR slice; ship Movistar as a follow-up slice (per-tenant routing already supports it). |
| Cost overrun from SMS volume | Medium | Add metrics + per-tenant monthly budget alert; out-of-scope here but documented as ops follow-up. |
| Existing simulated deliveries in production interfere with new state machine | Low | Migration leaves historical rows untouched; state machine only operates on new `Pending` rows; old `Sent` rows are immutable. |

## Rollback Plan

- **Backend**:
  - Feature flag `Notifications:RealProvidersEnabled = false` returns the dispatcher to a no-op (deliveries stay `Pending` — they are NOT silently marked `Sent`). This is a controlled rollback: operators see backlog instead of a fake success.
  - Webhook endpoint can be allow-listed off at the gateway / removed from the controller.
  - EF migration `down` removes the new columns (`NextAttemptOn`, `ProviderId`, `TenantId`) and the `TenantNotificationSettings` table; the new enum values (`Sending`, `DeadLetter`) are reverted by mapping back to `Pending` and `Failed` respectively (data migration in the `down` script).
- **Frontend**:
  - No frontend impact — this change is server-side only.
- **Operational**:
  - Provider API keys remain rotatable independent of the rollback.

## Dependencies

- Existing `customer-notifications` capability (current spec, `NotificationService`, `CustomerNotificationDeliveryEntity`).
- `BuildingBlocks` audit (`AuditService`, `PciAuditPublisher`) — already in use by the simulator path.
- Outbox + Kafka pipeline (already in use) — new events `cv.notification.send-attempt`, `cv.notification.deadletter`.
- Identity-and-access for the new `notifications:admin` permission.

## Unlocks (downstream)

- `secure-user-registration` — operator invitation emails go through `SendGridEmailProvider`.
- `fix-frontend-broken-features` — forgot-password reset emails go through `SendGridEmailProvider`.

## Success Criteria

- [ ] A real OTP SMS reaches a test phone (`+593` number routed through Movistar EC) when an OTP is generated.
- [ ] A real OTP SMS reaches a test phone (`+1` number routed through Twilio) when an OTP is generated.
- [ ] A real transactional email reaches a test mailbox when a transaction notification is created.
- [ ] When a provider returns `5xx` on the first attempt, the dispatcher retries with exponential backoff and eventually marks the delivery `Sent` (using a fault-injected adapter in tests).
- [ ] When a provider returns `4xx` on all attempts, the dispatcher transitions the delivery to `DeadLetter` after `MaxAttempts = 3` and emits `cv.notification.deadletter`.
- [ ] A signed delivery callback from each provider updates the delivery `Status` and `DeliveredOn`; an unsigned or tampered request is rejected with `401` and produces an audit event.
- [ ] A tenant configured with `MovistarEc` as primary uses Movistar; on simulated Movistar failure, the dispatcher falls back to Twilio for the same delivery.
- [ ] No secret material appears in committed configuration files.
- [ ] PCI audit events `pci.notification.send-attempt`, `pci.notification.send-result`, `pci.notification.delivery-confirmed`, `pci.notification.deadletter` are emitted on every state transition.
- [ ] Templates render correctly for `es-EC` and `en-US` and never contain raw PAN or raw OTP secret material.
- [ ] Backend tests cover: provider adapters (HTTP-mocked), webhook signature validators (positive + negative), dispatcher state machine, retry/backoff, registry routing, per-tenant fallback.
