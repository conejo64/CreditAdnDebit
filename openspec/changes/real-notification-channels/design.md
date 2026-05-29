# Technical Design — Real Notification Channels (SMS, Email)
## Change: `real-notification-channels`
## Capability: `customer-notifications`

---

## 0. Architecture Constraint (LOCKED)

CardVault is a **FLAT modular monolith**. This design does NOT use the empty `CardVault.Application` / `CardVault.Domain` stub projects.

- Provider classes, registry, renderer, state machine, webhook validators -> `CardVault.Api/Services/Notifications/`
- Entities and EF config -> `CardVault.Infrastructure.Persistence/Notifications/`
- Background worker stays at its REAL existing path: `CardVault.Api/Background/NotificationDispatcherWorker.cs` (the proposal's `Workers/` path was WRONG — corrected here).
- Webhook controller action -> existing `CardVault.Api/Controllers/NotificationsController.cs`.

### Codebase ground-truth corrections (verified against source — these OVERRIDE proposal/spec where they conflict)
1. **Attempt counter column is `Attempts` (int)** — already on `CustomerNotificationDeliveryEntity` (line 93). Spec used `AttemptCount`; design uses the REAL `Attempts`. `MaxAttempts` is a config constant, not a column.
2. **`DeliveredOn` (DateTimeOffset?) already exists** (line 103) — reused for delivery confirmation. No new column for it.
3. **Channel enum values are `Email = 1, Sms = 2`** (not `SMS`). Routing/registry keys use these.
4. **Worker is a 5s `Task.Delay` loop** calling `NotificationService.DispatchPendingDeliveriesAsync(50, ct)`. Simulator logic lives in the SERVICE (lines 92-158), not the worker. The rewrite moves real dispatch into a dedicated `INotificationDispatcher` invoked by the worker.
5. **Existing PCI event is `pci.notification.delivered`** via `PciAuditPublisher`. The spec introduces a NEW event set. Design: KEEP `pci.notification.delivered` (back-compat) AND add the four new events. Do NOT remove the old event.
6. **DbSet is `CustomerNotificationDeliveries`**; existing indexes `IX_(Status, CreatedOn)` and unique `IX_(NotificationId, Channel)`. Unique index preserved.

---

## 1. Approach & Architecture Pattern

**Strategy + Registry + persisted Finite State Machine, driven by an idempotent claim-based background worker.**
- Strategy = `INotificationProvider` (one adapter per provider; wire protocol fully encapsulated).
- Registry = `INotificationProviderRegistry` resolves an ordered provider chain per `(tenantId, channel)`.
- FSM = explicit 5-state machine persisted on every transition; guards reject illegal moves.
- Worker = existing `NotificationDispatcherWorker` delegates to a new scoped `INotificationDispatcher` that claims a batch, runs the FSM, persists.

### Layering inside the flat monolith
```
NotificationsController            -> webhook ingress (signature verify -> apply confirmation)
NotificationDispatcherWorker       -> 5s loop, scope-per-tick (existing)
  └─ INotificationDispatcher       -> claim batch, FSM, audit, outbox  (NEW, scoped)
       ├─ INotificationProviderRegistry  -> resolve chain per (tenant, channel)  (NEW, singleton)
       │    └─ INotificationProvider[]   -> Twilio / SendGrid / MovistarEc       (NEW, typed HttpClients)
       ├─ INotificationTemplateRenderer  -> Razor render, locale, PCI masking    (NEW, scoped)
       └─ IDeliveryStateMachine          -> transition guards + backoff calc      (NEW, stateless)
```

---

## 2. Core Interfaces (namespace `CardVault.Api.Services.Notifications`)

```csharp
public interface INotificationProvider
{
    string ProviderId { get; }              // "twilio" | "sendgrid" | "movistar-ec"
    NotificationChannel Channel { get; }    // Sms | Email
    bool CanHandle(string destinationE164OrEmail); // Movistar handles +593 prefixes
    Task<ProviderSendResult> SendAsync(NotificationSendRequest request, CancellationToken ct);
}

public sealed record NotificationSendRequest(
    Guid DeliveryId, Guid TenantId, NotificationChannel Channel,
    string Destination,        // unmasked E.164 / email — provider-only, NEVER logged/audited
    string RenderedSubject, string RenderedBody, string TemplateType, string Locale);

public sealed record ProviderSendResult(
    ProviderOutcome Outcome,   // Accepted | TransientFailure | PermanentFailure
    string? ProviderReference, string? ErrorCode, string? ErrorMessage, DateTimeOffset? ProviderReportedAt);

public enum ProviderOutcome { Accepted, TransientFailure, PermanentFailure }

public interface INotificationProviderRegistry
{ IReadOnlyList<INotificationProvider> ResolveChain(Guid tenantId, NotificationChannel channel, string destination); }

public interface INotificationDispatcher { Task<int> DispatchBatchAsync(int take, CancellationToken ct); }

public interface IDeliveryStateMachine
{
    bool CanTransition(NotificationDeliveryStatus from, NotificationDeliveryStatus to);
    void Transition(CustomerNotificationDeliveryEntity d, NotificationDeliveryStatus to);
    DateTimeOffset ComputeNextAttempt(int attempts, DateTimeOffset now);
}
```
The ADAPTER (not the dispatcher) classifies provider responses into Transient vs Permanent — isolates per-provider quirks (Open Q5: Movistar wire detail stays inside its adapter).
Registry is a singleton with a 5-min cache of `TenantNotificationSettings`. Slice 1: returns `[Twilio]` for SMS, `[SendGrid]` for Email (no DB lookup). Slice 2 adds Movistar + DB chain resolution.
DI: typed HttpClients per provider; `AddHostedService<NotificationDispatcherWorker>()` unchanged (only the worker body changes to call the dispatcher).

---

## 3. The 5-State Delivery State Machine (LOCKED — Open Q2)

**Exactly 5 states. `DeliveryConfirmed` is NOT a state** — it is a field update (`DeliveredOn`) on a row already `Sent`.
> SPEC CORRECTION (propagate to spec/tasks): the spec's webhook scenario says `Status = DeliveryConfirmed`. WRONG. Correct: webhook sets `DeliveredOn` + emits `pci.notification.delivery-confirmed`; `Status` stays `Sent`.

Enum extension (NEVER renumber existing 1/2/3):
```
Pending = 1, Sent = 2, Failed = 3,   // existing — DO NOT renumber
Sending = 4, DeadLetter = 5          // ADDED
```
Valid transitions:
```
Pending -> Sending | Sending -> Sent | Sending -> Failed | Failed -> Sending (retry)
Failed -> DeadLetter | Sending -> DeadLetter (permanent failure on fresh send)
DeadLetter terminal | Sent terminal for Status (DeliveredOn may still be set by webhook)
```
Illegal transition -> `InvalidDeliveryTransitionException`, NOT persisted, logged with deliveryId/from/to/caller.

### Crash recovery via `SendingStartedOn` lock TTL
New column `SendingStartedOn (DateTimeOffset?)` set on `Pending|Failed -> Sending`. Claim query also selects `Status==Sending AND SendingStartedOn < now - LockTtl` (default 5 min). Reclaim: `Sending -> Failed` (`LastError="dispatcher-crash-recovery"`, `Attempts++`), then backoff or DeadLetter. Single-instance optimistic guard; multi-instance row-version is future work.

---

## 4. EF Schema Delta

`CustomerNotificationDeliveryEntity` — ADDED columns:
| Column | Type | Notes |
|--------|------|-------|
| `Status` | enum int | extend with `Sending=4`, `DeadLetter=5` (enum only, no DDL) |
| `NextAttemptOn` | `DateTimeOffset?` | retry eligibility; null = immediate/none |
| `SendingStartedOn` | `DateTimeOffset?` | lock timestamp for crash recovery |
| `ProviderId` | `string?(32)` | which provider produced the terminal result |
| `TenantId` | `Guid` | routing key; backfill existing rows with default tenant |
Reused (NO change): `Attempts` (int), `DeliveredOn`, `LastError(256)`, `ProviderReference(128)`, `LastAttemptOn`.

EF config additions: `ProviderId` HasMaxLength(32); new indexes `(Status, NextAttemptOn)` (claim query), `(Status, SendingStartedOn)` (crash sweep), `(TenantId)`; keep existing indexes.

`TenantNotificationSettingsEntity` — NEW (Slice 2): Id, TenantId, Channel, ProviderId(32), Enabled, Priority(0=primary), CreatedOn. Unique `(TenantId, Channel, ProviderId)`, index `(TenantId, Channel, Priority)`.

Migrations: Slice 1 = `AddRealNotificationChannels` (4 columns + 3 indexes + TenantId backfill to default tenant). Slice 2 = `AddTenantNotificationSettings`. Down scripts remap `Sending(4)->Pending(1)`, `DeadLetter(5)->Failed(3)` before dropping.

---

## 5. Retry / Backoff
`MaxAttempts = 3` (config). `ComputeNextAttempt`: 1->2 = 30s ±10%; 2->3 = 2m ±10%; after 3 -> DeadLetter (10m grace for crash-reclaimed). Jitter via `Random.Shared`. Worker NEVER retries in-band — only sets `NextAttemptOn`; next tick re-claims. Claim eligibility: `Pending` OR (`Failed AND NextAttemptOn<=now AND Attempts<MaxAttempts`) OR (`Sending AND SendingStartedOn < now-LockTtl`).

---

## 6. Transient vs Permanent Classification (LOCKED — Open Q1)
**Override "all 4xx permanent": `429` is TRANSIENT and MUST be retried.** Adapter maps native codes:
| Provider | Transient (retry) | Permanent (-> DeadLetter) |
|----------|-------------------|----------------------------|
| Twilio | `429`; `5xx`; `20429`,`20503`; network/timeout | `4xx` except 429 — `21211`,`21610`,`21614`,`21408`,`21612` |
| SendGrid | `429`; `5xx`; network/timeout | `4xx` except 429 — `400`,`401/403`(alert),`413` |
| Movistar EC | `429`; `5xx`; SOAP `soap:Server`; `SYSTEM_BUSY`/`THROTTLED`; timeout | SOAP `soap:Client`; `INVALID_MSISDN`,`BLACKLISTED`,`AUTH_FAILED` |
Dispatcher precedence: Accepted->Sent; Permanent->DeadLetter (skip retries); Transient & Attempts<Max -> Failed+NextAttemptOn; Transient & Attempts>=Max -> next provider in chain, else DeadLetter. `429` additionally feeds a per-provider token-bucket limiter (anti retry-storm).

---

## 7. Per-Tenant Routing & Fallback Accounting (LOCKED — Open Q3)
**`MaxAttempts = 3` is SHARED across the ENTIRE provider chain, NOT per-provider.** `Attempts` is one counter spanning all providers tried.
ResolveChain: (1) TenantNotificationSettings for (tenant, channel) where Enabled, ordered by Priority; (2) filter by `CanHandle(destination)` (Movistar only +593); (3) append Twilio as global SMS fallback; (4) Slice 1: SMS=[Twilio], Email=[SendGrid].
Fallback: each provider call increments `Attempts` once. When `Attempts` reaches Max, advance to next provider WITHOUT resetting `Attempts`. Chain exhausted + budget spent -> DeadLetter. On success set `ProviderId`, `Sent`.
> Accounting example: Movistar fails (Attempts 1,2,3) -> chain advances to Twilio with budget spent -> Twilio gets exactly ONE fallback attempt before DeadLetter. (Documented for task assertions. "One attempt per provider" is a future change.)

---

## 8. Webhook Ingress & Signature Validation
Endpoint `POST /api/notifications/delivery-callback/{providerId}` on `NotificationsController`. `[AllowAnonymous]` (provider auth = signature). `providerId` validated against closed set; unknown -> 404. Rate-limited (Open Q4).
`IWebhookSignatureValidator` per provider:
| Provider | Scheme | Secret source |
|----------|--------|---------------|
| Twilio | `X-Twilio-Signature` = Base64(HMAC-SHA1(AuthToken, fullUrl + sorted params)) | `Notifications__Providers__Twilio__AuthToken` |
| SendGrid | Event Webhook **ECDSA** (`X-Twilio-Email-Event-Webhook-Signature` + `-Timestamp`) verified with public key over `timestamp + rawBody` (NOTE: ECDSA, not HMAC — "HMAC" in spec is loose) | `__SendGrid__WebhookPublicKey` |
| Movistar EC | HMAC-SHA256 over rawBody, header `X-Movistar-Signature` (per B2B contract) | `__MovistarEc__WebhookSecret` |
Validation order (deny-by-default): resolve validator (none->404); missing sig -> 401 audit `missing-signature` no DB touch; mismatch -> 401 audit `invalid-signature`; replay (timestamp older than 5 min) -> 401 audit `replayed`; valid -> set `DeliveredOn` on the `Sent` row, emit `pci.notification.delivery-confirmed`, 200.

### Webhook rate-limit (Open Q4) — policy `notifications_webhook`, partition by providerId
| providerId | Permit | Window | Queue |
|-----------|--------|--------|-------|
| sendgrid | 600 | 1 min | 0 |
| twilio | 300 | 1 min | 0 |
| movistar-ec | 120 | 1 min | 0 |
| unknown fallback | 60 | 1 min | 0 |

---

## 9. Razor Template Rendering (PCI-safe)
`RazorNotificationTemplateRenderer` using `RazorLight` (self-contained, no MVC view engine). Templates under `Services/Notifications/Templates/{TemplateType}.{locale}.cshtml`. Types: OTP, TransactionNotification, SecurityAlert, StatementAvailable, PaymentReceived. Locales es-EC/en-US; null/unsupported -> es-EC.
**PCI masking enforced at the MODEL boundary**: renderer accepts only a sealed `TemplateModel` with pre-masked PAN (`****NNNN`); a pre-render validator rejects any field matching unmasked-PAN regex (`\d{6,}`) or OTP seed/secret -> `PciTemplateViolationException`. Permitted: last-4 PAN, amount, masked merchant, timestamp, displayable OTP code (never the seed). Renderer never logs the body.

---

## 10. Secrets Handling
ALL secrets via env vars / vault, NEVER in committed appsettings. Local dev `dotnet user-secrets`. Keys (double-underscore): `Notifications__Providers__Twilio__AuthToken|AccountSid`, `__SendGrid__ApiKey|WebhookPublicKey`, `__MovistarEc__ApiKey|WebhookSecret`. appsettings holds ONLY non-secret fields (base URL, sender id, template ids, `RealProvidersEnabled`, dispatcher tuning). CI guard greps committed config for `SG\.` and `AC[0-9a-f]{32}` -> fail on match. `RealProvidersEnabled=false` -> dispatcher leaves rows `Pending` (visible backlog), NEVER fakes `Sent`.

---

## 11. Audit & Outbox Events
PCI events via `PciAuditPublisher`: `pci.notification.send-attempt` (on ->Sending, before provider call), `pci.notification.send-result` (after response, outcome=sent|failed, includes providerReference/errorCode), `pci.notification.delivery-confirmed` (valid webhook), `pci.notification.deadletter` (->DeadLetter). KEEP existing `pci.notification.delivered`. Each event: deliveryId, notificationId, tenantId, channel, providerId?, attempts, UTC ts. EXCLUDES raw PAN, OTP, destination, body.
Outbox: keep `cv.customer.notification.delivered`; ADD `cv.notification.deadletter`. Rejected webhooks also audited via `AuditService.WriteAsync` with reason.

---

## 12. Slice Plan (boundary for sdd-tasks)
### Slice 1 (PR #1) — Twilio + SendGrid + FSM + retry + audit + webhooks
enum extension; migration `AddRealNotificationChannels`; INotificationProvider/ProviderSendResult; TwilioSmsProvider, SendGridEmailProvider (typed HttpClients); IDeliveryStateMachine + guards + backoff; INotificationDispatcher (claim, FSM, single-provider fallback, audit, outbox); worker body swap; remove simulator branch in DispatchPendingDeliveriesAsync; RazorNotificationTemplateRenderer + 5 templates ×2 locales + PCI guard; webhook action + Twilio/SendGrid validators + `notifications_webhook` policy; config skeleton + secrets; RealProvidersEnabled flag; PCI events. Registry returns fixed `[Twilio]`/`[SendGrid]`. Self-contained: no Movistar, no DB routing.
### Slice 2 (PR #2) — Movistar EC + per-tenant routing
TenantNotificationSettingsEntity + EF + migration `AddTenantNotificationSettings`; MovistarEcuadorSmsProvider (SOAP/REST adapter, DLR or degraded confirmation); MovistarSignatureValidator; DB-backed ResolveChain (primary + fallback, Twilio tail); `notifications:admin` permission + tenant-settings management endpoint. Depends on Slice 1.

---

## 13. Movistar EC Wire Protocol — Adapter Isolation (Open Q5)
`MovistarEcuadorSmsProvider` fully hides SOAP vs REST behind `INotificationProvider`. If SOAP: hand-rolled `XDocument`/`HttpClient` POST (no WCF). If REST: typed HttpClient JSON. **Degraded confirmation**: if Movistar is synchronous-only with NO DLR callback, `SendAsync` returning `Accepted` is the FINAL confirmation — row -> `Sent`, `DeliveredOn` set at send, `pci.notification.delivery-confirmed` emitted at send time. State machine unaffected (still 5 states). Logged as a known SBS-evidence limitation for Movistar-routed messages.

---

## 14. Test Strategy (Strict TDD — `dotnet test backend/CardSwitchPlatform.sln`)
Tests under `CardVault.Tests/Features/Notifications/`. Provider adapters HTTP-mocked (assert request shape + outcome mapping incl. 429-as-transient, 4xx-permanent). State machine pure unit (legal/illegal transitions, jitter bounds). Dispatcher fault injection (`5xx->5xx->Accepted` proves retry-then-Sent; permanent -> immediate DeadLetter; crash recovery via aged SendingStartedOn). Fallback accounting (Movistar fail -> Twilio succeed, shared budget). Webhook validators (positive, missing/tampered/replayed 401, unknown 404, constant-time). Templates (es-EC/en-US render, PCI guard rejects unmasked PAN/OTP, locale fallback). Config/secrets grep guard. Use existing `CardVaultWebApplicationFactory` for webhook/endpoint integration.

---

## 15. ADRs
| # | Decision | Rationale | Rejected |
|---|----------|-----------|----------|
| ADR-1 | Dispatch in hosted worker via `INotificationDispatcher` | Durable retry across restarts; back-pressure; failure isolation | In-band send (loses retry on crash) |
| ADR-2 | 5-state FSM; `DeliveredOn` field update, NOT a 6th state | Confirmation is metadata on a `Sent` row | 6th state (spec literal) — corrupts terminal semantics |
| ADR-3 | `429` TRANSIENT; other 4xx permanent, per-provider matrix in adapter | Rate-limit recoverable; bad-number must not burn retries | "all 4xx permanent" |
| ADR-4 | `MaxAttempts=3` SHARED across chain | Single bounded budget; predictable cost | Per-provider budget (unbounded fan-out) |
| ADR-5 | Provider abstraction hides Movistar wire; degrade if no DLR | Wire detail must not leak into dispatcher | Branching dispatcher on protocol |
| ADR-6 | PCI masking at template MODEL boundary + regex guard | Defense-in-depth; don't trust authors | Trust templates (one mistake = PAN leak) |
| ADR-7 | RazorLight, not MVC ViewEngine | API host has no MVC view stack | Wiring full MVC views |
| ADR-8 | Reclaim via SendingStartedOn + LockTtl, no distributed lock | Single instance today | Redis/DB advisory lock (premature) |
| ADR-9 | Keep `pci.notification.delivered`, ADD new event set | Back-compat for consumers | Replace old event (breaks consumers) |

---

## 16. Risks
- Enum renumbering hazard: append 4/5 only; never reorder 1/2/3.
- TenantId backfill: existing rows need a default tenant; confirm source if not single-tenant.
- SendGrid signature is ECDSA, not HMAC: tasks/tests must use the ECDSA public-key path.
- Movistar wire & DLR uncertainty: resolved at Slice 2; degraded-confirmation path documented.
- Shared-budget fallback gives the tail provider only one attempt once budget spent — confirm SLA.
- Spec divergences to reconcile in tasks: `Status=DeliveryConfirmed` (ADR-2), `all 4xx permanent` (ADR-3), `AttemptCount` vs real `Attempts`, `Workers/` vs real `Background/` path.
