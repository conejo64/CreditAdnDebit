# Tasks — Real Notification Channels (SMS, Email)
## Change: `real-notification-channels`
## Generated: 2026-05-30
## Artifact store: openspec (file-only — Engram unavailable)

---

## Spec Corrections Carried Forward (design overrides)

These corrections from the design doc OVERRIDE the spec where they conflict. Task assertions must use the design values:

| Topic | Spec said | Correct (design) |
|-------|-----------|-----------------|
| Column name | `AttemptCount` | `Attempts` (already on entity, line 93) |
| Webhook outcome state | `Status = DeliveryConfirmed` | `Status` stays `Sent`; webhook only sets `DeliveredOn` |
| SendGrid signature scheme | "HMAC" | ECDSA (`X-Twilio-Email-Event-Webhook-Signature` + Timestamp) |
| Worker path | `Workers/` | `Background/` (existing `NotificationDispatcherWorker.cs`) |
| Sending→Sent guard | providerReference optional | providerReference REQUIRED for Sending→Sent transition |

---

## Proposed PR Slice Plan

| # | Slice | Depends on | Est. prod lines | Est. test lines | Independently delivers | Unblocks email? |
|---|-------|-----------|----------------|----------------|----------------------|-----------------|
| 1a | Abstraction + FSM + fake provider + registry stub | — | ~250 | ~350 | Testable FSM, fake dispatch loop, TDD scaffold | No |
| 1b | SendGrid email adapter + Twilio SMS adapter | 1a | ~350 | ~300 | Real email + SMS via Twilio; simulator removed | **YES — EMAIL UNBLOCKED** |
| 1c | Razor templates + PCI guard (5 types × 2 locales) | 1a | ~400 | ~200 | Rendered email/SMS bodies with PCI safety | No (but required before 1b goes to prod) |
| 1d | Dispatcher worker rewrite + EF migration + retry/backoff | 1a, 1b, 1c | ~380 | ~350 | End-to-end dispatch: Pending→Sending→Sent/Failed/DeadLetter | No |
| 1e | Inbound webhooks + HMAC/ECDSA validation + rate-limit | 1b, 1d | ~280 | ~250 | Signed delivery callbacks; 401 on tamper/replay | No |
| 2a | Movistar EC SMS adapter + SOAP/REST isolation | 1b | ~220 | ~180 | Movistar Ecuador channel (degraded-ok if no DLR) | No |
| 2b | TenantNotificationSettings entity + migration + DB routing | 1d, 2a | ~250 | ~200 | Per-tenant primary/fallback chain, admin endpoint | No |
| 2c | Metrics + trace propagation + spec sync + docs | 1d, 1e, 2b | ~150 | ~50 | Full observability; spec.md reconciled | No |

**Total estimated: ~2,280 prod lines + ~1,880 test lines ≈ 4,160 changed lines**
**400-line budget risk: HIGH — chained PRs REQUIRED (confirmed: auto-chain strategy)**
**Chained PRs recommended: Yes**

> CRITICAL EMAIL DEPENDENCY: Slice 1b (SendGrid adapter) is the gate for `secure-user-registration` and `fix-frontend-broken-features`. Ship 1a+1b+1c+1d together as PR #1 (or at minimum 1a→1b sequentially) to unblock those downstream changes.

---

## Slice 1a — Abstraction + FSM + Fake Provider + Registry Stub

**Goal**: Establish the full testability scaffold. Every downstream slice builds on these interfaces.
No real HTTP calls. No EF changes yet. Compiles and tests pass in isolation.

### Task 1a.1 — Core interfaces + value types
- [x] Write failing tests: `INotificationProvider` contract (ProviderId, Channel, CanHandle, SendAsync), `ProviderSendResult` record, `ProviderOutcome` enum, `NotificationSendRequest` record
  - File: `CardVault.Tests/Features/Notifications/Abstractions/NotificationProviderContractTests.cs`
- [x] Create `CardVault.Api/Services/Notifications/INotificationProvider.cs` with interfaces `INotificationProvider`, `INotificationProviderRegistry`, `INotificationDispatcher`, `IDeliveryStateMachine`, `IWebhookSignatureValidator`
- [x] Create value records: `NotificationSendRequest`, `ProviderSendResult`, enum `ProviderOutcome`
- [x] Tests pass: `dotnet test` green
- **Spec ref**: Design §2 (Core Interfaces)

### Task 1a.2 — Extend NotificationDeliveryStatus enum + InvalidDeliveryTransitionException
- [x] Write failing tests: enum values `Sending = 4`, `DeadLetter = 5` exist and do NOT renumber 1/2/3; `InvalidDeliveryTransitionException` carries deliveryId/from/to/caller
  - File: `CardVault.Tests/Features/Notifications/StateMachine/DeliveryStatusEnumTests.cs`
- [x] Add `Sending = 4, DeadLetter = 5` to `NotificationDeliveryStatus` in `CustomerNotificationEntity.cs`
- [x] Create `CardVault.Api/Services/Notifications/InvalidDeliveryTransitionException.cs`
- [x] Tests pass
- **Spec ref**: Design §3 (5-State FSM); IMPORTANT: NEVER renumber existing 1/2/3

### Task 1a.3 — IDeliveryStateMachine implementation + pure unit tests
- [x] Write failing tests FIRST (controllable-clock pattern via `Func<DateTimeOffset>` parameter):
  - Legal transitions: Pending→Sending, Sending→Sent, Sending→Failed, Failed→Sending (retry), Failed→DeadLetter, Sending→DeadLetter
  - Illegal transitions throw `InvalidDeliveryTransitionException` (Sent→Sending, Sent→Pending, DeadLetter→anything, etc.)
  - `ComputeNextAttempt(attempts: 1, now)` returns `now + 30s` with jitter within ±10% (assert range)
  - `ComputeNextAttempt(attempts: 2, now)` returns `now + 2m` within ±10%
  - File: `CardVault.Tests/Features/Notifications/StateMachine/DeliveryStateMachineTests.cs`
- [x] Create `CardVault.Api/Services/Notifications/DeliveryStateMachine.cs` implementing `IDeliveryStateMachine`
  - Inject `Func<DateTimeOffset>` clock for testability (default: `() => DateTimeOffset.UtcNow`)
  - Jitter via `Random.Shared`
  - Transition sets `SendingStartedOn` when entering `Sending`; clears it on `Sent`/`Failed`/`DeadLetter`
- [x] Tests pass
- **Spec ref**: Design §3, §5; Spec "Delivery State Machine" requirement

### Task 1a.4 — FakeNotificationProvider + FakeProviderRegistry (test doubles)
- [x] Write usage tests: registry resolves fake for SMS and Email; fake returns `Accepted` by default; can be configured to return `TransientFailure` or `PermanentFailure` for fault-injection
  - File: `CardVault.Tests/Features/Notifications/Fakes/FakeNotificationProviderTests.cs`
- [x] Create `CardVault.Tests/Features/Notifications/Fakes/FakeNotificationProvider.cs`
  - Implements `INotificationProvider`; ProviderId = "fake"; configurable outcome queue (dequeue per call)
  - Records calls for assertion (`IReadOnlyList<NotificationSendRequest> Calls`)
- [x] Create `CardVault.Tests/Features/Notifications/Fakes/FakeProviderRegistry.cs`
  - Returns `[FakeNotificationProvider]` for any channel; injectable
- [x] Tests pass
- **Spec ref**: Design §14 (Test Strategy — "Provider adapters HTTP-mocked")

### Task 1a.5 — NotificationProviderRegistry (stub — fixed chains)
- [x] Write failing tests: registry for SMS returns chain containing a Twilio provider; for Email returns SendGrid; `CanHandle` filter respected (Movistar only for +593, to be wired in Slice 2a)
  - File: `CardVault.Tests/Features/Notifications/Registry/NotificationProviderRegistryTests.cs`
- [x] Create `CardVault.Api/Services/Notifications/NotificationProviderRegistry.cs` implementing `INotificationProviderRegistry`
  - Slice-1 stub: SMS → `[TwilioSmsProvider]`, Email → `[SendGridEmailProvider]` (placeholder registration, actual adapters in 1b)
  - 5-min in-memory cache entry (no DB lookup yet — Slice 2b adds DB)
  - Singleton lifetime
- [x] Register as singleton in DI (Program.cs skeleton — wire providers as keyed services)
- [x] Tests pass
- **Spec ref**: Design §2 (Registry), §12 (Slice 1 boundary)

---

## Slice 1b — SendGrid Email Adapter + Twilio SMS Adapter (EMAIL UNBLOCKED)

**Goal**: Real provider calls. HTTP-mocked in tests. No Movistar. After this slice, `secure-user-registration` and `fix-frontend-broken-features` can consume `SendGridEmailProvider`. Depends on 1a.

### Task 1b.1 — NotificationDispatcherOptions + appsettings skeleton + secrets check
- [x] Write failing test: `NotificationDispatcherOptions` binds from `appsettings.json`; `RealProvidersEnabled` flag exists; CI grep guard test (`appsettings.json` contains no `SG.` or `AC[0-9a-f]{32}` pattern)
  - File: `CardVault.Tests/Features/Notifications/Config/NotificationConfigTests.cs`
- [x] Create `CardVault.Api/Services/Notifications/NotificationDispatcherOptions.cs`
  - `RealProvidersEnabled (bool)`, `MaxAttempts (int = 3)`, `LockTtlMinutes (int = 5)`, `BatchSize (int = 50)`
- [x] Create `CardVault.Api/Services/Notifications/Providers/TwilioOptions.cs` (AccountSid, FromNumber, StatusCallbackUrl — NO AuthToken here)
- [x] Create `CardVault.Api/Services/Notifications/Providers/SendGridOptions.cs` (FromEmail, FromName, TemplateIdMap — NO ApiKey here)
- [x] Add non-secret config skeleton to `appsettings.json` under `Notifications:Providers:{Twilio|SendGrid}`
- [x] Register options via `services.Configure<T>` in Program.cs
- [x] Tests pass (including the grep guard)
- **Spec ref**: Design §10, Spec "Secrets and Provider Configuration"

### Task 1b.2 — SendGridEmailProvider adapter
- [x] Write failing tests (use `HttpMessageHandler` mock — `WireMock.Net` or `MockHttpMessageHandler`):
  - Happy path: `SendAsync` posts to SendGrid API; result is `Accepted` with non-null `ProviderReference` (message-id from response)
  - `5xx` → `TransientFailure`
  - `4xx` (400, 413) → `PermanentFailure`
  - `429` → `TransientFailure` (NOT permanent — ADR-3)
  - `401/403` → `PermanentFailure` (alert-worthy but permanent for this delivery)
  - Network timeout → `TransientFailure`
  - File: `CardVault.Tests/Features/Notifications/Providers/SendGridEmailProviderTests.cs`
- [x] Create `CardVault.Api/Services/Notifications/Providers/SendGridEmailProvider.cs`
  - Implements `INotificationProvider`; ProviderId = "sendgrid"; Channel = Email
  - Typed `HttpClient` (registered via `AddHttpClient<SendGridEmailProvider>`)
  - Auth token from `IApiKeyProvider` resolving env var `Notifications__Providers__SendGrid__ApiKey`
  - `CanHandle`: returns `true` for any non-empty email (no filtering)
  - Maps response to `ProviderOutcome` per design §6 classification table
- [x] Register typed HttpClient in Program.cs
- [x] Tests pass
- **Spec ref**: Design §6 (SendGrid classification table), Spec scenario "Transactional email reaches cardholder via SendGrid"

### Task 1b.3 — TwilioSmsProvider adapter
- [x] Write failing tests (HTTP-mocked):
  - Happy path: posts to `https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json`; `Accepted` with SID as `ProviderReference`
  - `5xx` → `TransientFailure`
  - Twilio error codes `21211`, `21610`, `21614`, `21408`, `21612` → `PermanentFailure`
  - `429` / Twilio `20429` / `20503` → `TransientFailure`
  - `4xx` not in permanent list → `PermanentFailure`
  - File: `CardVault.Tests/Features/Notifications/Providers/TwilioSmsProviderTests.cs`
- [x] Create `CardVault.Api/Services/Notifications/Providers/TwilioSmsProvider.cs`
  - Implements `INotificationProvider`; ProviderId = "twilio"; Channel = Sms
  - Typed `HttpClient` with Basic auth (AccountSid:AuthToken from env `Notifications__Providers__Twilio__AuthToken`)
  - `CanHandle`: always true (global fallback)
  - Classifies responses per design §6 Twilio table
- [x] Register typed HttpClient in Program.cs
- [x] Wire both providers into `NotificationProviderRegistry` (replace stubs from 1a.5)
- [x] Tests pass
- **Spec ref**: Design §6 (Twilio classification), Spec scenario "OTP SMS reaches +1 cardholder via Twilio"
- **UNBLOCKS**: `secure-user-registration`, `fix-frontend-broken-features` (Email channel live)

---

## Slice 1c — Razor Templates + PCI Guard

**Goal**: All 5 template types × 2 locales, PCI model boundary enforced. Depends on 1a only (no real providers needed for rendering).

### Task 1c.1 — TemplateModel + PCI pre-render guard
- [x] Write failing tests:
  - `TemplateModel` with masked PAN (`****1234`) passes guard
  - `TemplateModel` with unmasked PAN (`4111111111111111`) → `PciTemplateViolationException`
  - `TemplateModel` with field matching `\d{6,}` → `PciTemplateViolationException`
  - `TemplateModel` with OTP seed/secret field set → `PciTemplateViolationException`
  - File: `CardVault.Tests/Features/Notifications/Templates/PciTemplateGuardTests.cs`
- [x] Create `CardVault.Api/Services/Notifications/Templates/TemplateModel.cs` (sealed record)
  - Properties: `string? MaskedPan`, `decimal? Amount`, `string? CurrencyCode`, `string? MaskedMerchant`, `DateTimeOffset? Timestamp`, `string? OtpCode` (display-only), `string TemplateType`, `string Locale`, `string? AdditionalData`
- [x] Create `CardVault.Api/Services/Notifications/Templates/PciTemplateGuard.cs`
  - Regex: `\d{6,}` rejects any field; hyphen/space stripping catches formatted PANs; OTP seed detection heuristic
- [x] Tests pass (18/18 green)
- **Spec ref**: Design §9, Spec "PCI-Safe Templating" requirement

### Task 1c.2 — RazorNotificationTemplateRenderer + 10 template files
- [x] Write failing tests (integration-style, no web host needed — RazorLight directly):
  - OTP `es-EC` template renders `OtpCode` visible, no PAN
  - OTP `en-US` template renders in English
  - TransactionNotification `es-EC` contains masked PAN format `****NNNN` and amount
  - Locale fallback: null locale → `es-EC`
  - Unsupported locale (e.g., `fr-FR`) → `es-EC`
  - PCI guard integrated: renderer rejects model with unmasked PAN before render
  - File: `CardVault.Tests/Features/Notifications/Templates/RazorNotificationTemplateRendererTests.cs`
- [x] Add NuGet package `RazorLight` 2.3.1 (latest stable)
- [x] Create `CardVault.Api/Services/Notifications/Templates/RazorNotificationTemplateRenderer.cs` implementing `INotificationTemplateRenderer`
  - Template discovery via `FileSystemProject` rooted at `AppContext.BaseDirectory/Services/Notifications/Templates/`
  - Files use underscore locale convention (`es_EC`, `en_US`) to be valid filenames
  - Locale negotiation: exact match → `es-EC` fallback
  - Calls `PciTemplateGuard.Validate(model)` before render
  - NEVER logs rendered body
- [x] Create 10 template files (using `_` locale separator for filenames):
  - `Templates/Otp.es_EC.cshtml`, `Templates/Otp.en_US.cshtml`
  - `Templates/TransactionNotification.es_EC.cshtml`, `Templates/TransactionNotification.en_US.cshtml`
  - `Templates/SecurityAlert.es_EC.cshtml`, `Templates/SecurityAlert.en_US.cshtml`
  - `Templates/StatementAvailable.es_EC.cshtml`, `Templates/StatementAvailable.en_US.cshtml`
  - `Templates/PaymentReceived.es_EC.cshtml`, `Templates/PaymentReceived.en_US.cshtml`
  - All set `CopyToOutputDirectory=PreserveNewest` via csproj
- [x] Register `INotificationTemplateRenderer` as scoped in Program.cs; `PciTemplateGuard` as singleton
- [x] Tests pass (14 renderer + 18 guard = 32 new tests green; total 449)
- **Spec ref**: Design §9 (Razor+RazorLight, PCI masking, locales), Spec "PCI-Safe Templating" scenarios

---

## Slice 1d — Dispatcher Worker Rewrite + EF Migration + Retry/Backoff

**Goal**: End-to-end dispatch. Replaces the simulator. Depends on 1a, 1b, 1c. This is the core of the change.

### Task 1d.1 — EF entity delta + migration AddRealNotificationChannels
- [x] Write failing tests:
  - `CustomerNotificationDeliveryEntity` has properties `NextAttemptOn (DateTimeOffset?)`, `SendingStartedOn (DateTimeOffset?)`, `ProviderId (string? max 32)`, `TenantId (Guid)`
  - EF config: `ProviderId` HasMaxLength(32); indexes `(Status, NextAttemptOn)`, `(Status, SendingStartedOn)`, `(TenantId)` added; existing indexes unchanged
  - Migration `AddRealNotificationChannels` has a `Down` that maps `Sending(4)→Pending(1)` and `DeadLetter(5)→Failed(3)` before dropping columns
  - File: `CardVault.Tests/Features/Notifications/Persistence/NotificationDeliveryEntityTests.cs`
- [x] Add 4 new properties to `CustomerNotificationDeliveryEntity.cs` (do NOT remove or rename existing properties)
- [x] Add EF configuration to `CardVaultDbContext` or a separate `IEntityTypeConfiguration` for the delivery entity:
  - `ProviderId.HasMaxLength(32)`
  - `.HasIndex(e => new { e.Status, e.NextAttemptOn }).HasDatabaseName("IX_CustomerNotificationDeliveries_Status_NextAttemptOn")`
  - `.HasIndex(e => new { e.Status, e.SendingStartedOn }).HasDatabaseName("IX_CustomerNotificationDeliveries_Status_SendingStartedOn")`
  - `.HasIndex(e => e.TenantId).HasDatabaseName("IX_CustomerNotificationDeliveries_TenantId")`
  - Keep existing `IX_(Status, CreatedOn)` and unique `IX_(NotificationId, Channel)`
- [x] Run `dotnet ef migrations add AddRealNotificationChannels` and write the manual `Down` data migration
- [x] Add `TenantId` backfill comment in migration (single-tenant: use config default or `Guid.Empty` — document clearly)
- [x] Tests pass (EF model validation test, not an actual DB test)
- **Spec ref**: Design §4 (EF Schema Delta)

### Task 1d.2 — INotificationDispatcher: claim + FSM + audit events
- [x] Write failing tests with `FakeNotificationProvider` + fake clock + in-memory DB:
  - `DispatchBatchAsync` picks `Pending` rows and transitions to `Sending` BEFORE provider call (persisted)
  - On `Accepted`: transitions to `Sent`, sets `ProviderId`, `ProviderReference`, clears `SendingStartedOn`
  - On `TransientFailure` + `Attempts < MaxAttempts`: transitions to `Failed`, sets `NextAttemptOn` (30s ±10% for attempt 1)
  - On `PermanentFailure`: transitions directly to `DeadLetter`, emits `cv.notification.deadletter` outbox event
  - Claim also selects `Failed` rows where `NextAttemptOn <= now AND Attempts < MaxAttempts`
  - Crash recovery: `Sending` rows where `SendingStartedOn < now - LockTtl` are reclaimed → `Failed` with `LastError = "dispatcher-crash-recovery"`, `Attempts++`
  - `RealProvidersEnabled = false` → dispatcher returns 0 dispatched (no-op, rows stay `Pending`)
  - PCI events emitted: `pci.notification.send-attempt` before provider call; `pci.notification.send-result` after; `pci.notification.deadletter` on DeadLetter
  - File: `CardVault.Tests/Features/Notifications/Dispatcher/NotificationDispatcherTests.cs`
- [x] Create `CardVault.Api/Services/Notifications/NotificationDispatcher.cs` implementing `INotificationDispatcher`
  - Scoped lifetime (scope-per-tick from worker)
  - Injects: `CardVaultDbContext`, `INotificationProviderRegistry`, `IDeliveryStateMachine`, `INotificationTemplateRenderer`, `PciAuditPublisher`, `ILogger`, `IOptions<NotificationDispatcherOptions>`, `IEventBus`
  - Claim query: `Status == Pending OR (Status == Failed AND NextAttemptOn <= now AND Attempts < MaxAttempts) OR (Status == Sending AND SendingStartedOn < now - LockTtl)`
  - `RealProvidersEnabled = false` → return 0 immediately (no fake Sent, no sim-* references)
  - Per-provider token-bucket check for 429 anti-storm (simple in-memory per-provider counter, singleton)
  - Outbox event: `cv.notification.deadletter` on DeadLetter transition
  - Keep existing `cv.customer.notification.delivered` outbox event on `Sent`
  - Keep existing `pci.notification.delivered` event (back-compat; ADD new events alongside it — ADR-9)
- [x] Tests pass (all fault-injection scenarios green)
- **Spec ref**: Design §1 (Layering), §5 (Retry/Backoff), §11 (Audit/Outbox), §12 (Slice 1), Spec all dispatcher scenarios

### Task 1d.3 — NotificationDispatcherWorker body swap
- [x] Write failing test: worker creates scope per tick, calls `INotificationDispatcher.DispatchBatchAsync(50, ct)`, NOT `NotificationService.DispatchPendingDeliveriesAsync`
  - File: `CardVault.Tests/Features/Notifications/Dispatcher/NotificationDispatcherWorkerTests.cs`
- [x] Modify `CardVault.Api/Background/NotificationDispatcherWorker.cs`:
  - Resolve `INotificationDispatcher` from scope (not `NotificationService`)
  - Call `dispatcher.DispatchBatchAsync(50, stoppingToken)`
  - Worker loop and 5s delay unchanged
- [x] Modify `CardVault.Api/Services/NotificationService.cs`:
  - Remove simulator body from `DispatchPendingDeliveriesAsync` (lines 92-158 simulator logic)
  - The method can become a thin wrapper or be removed entirely (prefer removal; adjust any remaining callers)
- [x] Register `INotificationDispatcher` as scoped in Program.cs
- [x] Tests pass; `dotnet build` clean
- **Spec ref**: Design §4 ("Worker is a 5s Task.Delay loop...rewrite moves real dispatch into INotificationDispatcher"), Spec "Simulator references are rejected in production"

### Task 1d.4 — Fallback chain accounting unit tests
- [x] Write failing tests (shared-budget, not per-provider):
  - Movistar fail (Attempts: 1→2→3) → chain advances to Twilio with budget spent → Twilio gets ONE attempt → if Twilio fails → DeadLetter
  - Twilio succeeds after Movistar fail → Sent, `ProviderId = "twilio"`
  - Use `FakeNotificationProvider` with outcome queues: [Transient, Transient, Transient] for Movistar, [Accepted] for Twilio
  - File: `CardVault.Tests/Features/Notifications/Dispatcher/ProviderFallbackAccountingTests.cs`
- [x] Ensure `NotificationDispatcher` correctly advances through provider chain on Transient failures using the shared `Attempts` counter (no per-provider reset)
- [x] Tests pass
- **Spec ref**: Design §7 (Fallback Accounting — "MaxAttempts=3 SHARED across entire chain"), §6 (Dispatcher precedence)

---

## Slice 1e — Inbound Webhooks + Signature Validation + Rate-Limit

**Goal**: `POST /api/notifications/delivery-callback/{providerId}`. HMAC (Twilio), ECDSA (SendGrid), HMAC-SHA256 (Movistar EC). Depends on 1b and 1d.

### Task 1e.1 — IWebhookSignatureValidator + per-provider implementations
- [x] Write failing unit tests (known secrets + pre-computed signatures — NEVER call real providers):
  - **Twilio**: HMAC-SHA1(AuthToken, fullUrl + sorted params); positive, missing header → fail, tampered → fail, replay (timestamp > 5 min old) → fail
  - **SendGrid**: ECDSA public-key over `timestamp + rawBody` (`X-Twilio-Email-Event-Webhook-Signature` + Timestamp header); positive, invalid sig → fail, replay → fail
  - **Movistar EC**: HMAC-SHA256(WebhookSecret, rawBody), header `X-Movistar-Signature`; same positive/negative/replay coverage
  - All validators: constant-time comparison (verify no timing leak assertion in test description)
  - File: `CardVault.Tests/Features/Notifications/Webhooks/WebhookSignatureValidatorTests.cs`
- [x] Create `CardVault.Api/Services/Notifications/Webhooks/IWebhookSignatureValidator.cs` (already defined in `INotificationProvider.cs` from Slice 1a)
- [x] Create `TwilioWebhookSignatureValidator.cs`, `SendGridWebhookSignatureValidator.cs`, `MovistarWebhookSignatureValidator.cs`
  - Each reads its secret from `IOptions<T>` bound to env vars (not appsettings)
  - Replay guard: compare timestamp header vs `DateTimeOffset.UtcNow - 5min`
  - Shared `WebhookValidatorHelper.IsWithinReplayWindow` (DRY, boundary at < 300s)
- [x] Tests pass (27 new tests: 8 Twilio + 8 SendGrid + 11 Movistar; 540 total suite, 0 failures)
- **Spec ref**: Design §8 (Webhook signature schemes table), Spec "Inbound Delivery Callbacks" requirement — NOTE: SendGrid uses ECDSA not HMAC

### Task 1e.2 — Webhook endpoint + rate-limit policy + audit events
- [x] Write failing integration tests using `CardVaultWebApplicationFactory` (inject fake signature validators):
  - Valid signed Twilio callback → 200; `DeliveredOn` updated; `pci.notification.delivery-confirmed` emitted
  - Valid signed SendGrid callback → 200
  - Valid signed Movistar callback → 200
  - Missing signature header → 401; audit event `reason = "missing-signature"`; no DB write
  - Tampered signature → 401; audit event `reason = "invalid-signature"`; no DB write
  - Unknown providerId (`POST .../unknown-provider`) → 404
  - Rate-limit exceeded (sendgrid >600/min, twilio >300/min) → 429
  - File: `CardVault.Tests/Features/Notifications/Webhooks/WebhookEndpointIntegrationTests.cs`
- [x] Add `POST /api/notifications/delivery-callback/{providerId}` action to `CardVault.Api/Controllers/NotificationsController.cs`:
  - `[AllowAnonymous]` attribute
  - `[EnableRateLimiting("notifications_webhook")]`
  - Resolves validator from keyed DI by `providerId`; unknown → 404
  - Validation order: resolve → missing-sig → tampered → replay → valid
  - On valid: set `DeliveredOn`; emit `pci.notification.delivery-confirmed`; 200
  - On invalid: `AuditService.WriteAsync` with reason; 401
- [x] Add `notifications_webhook` rate-limit policy to Program.cs:
  - Partition key: `providerId` from route
  - Limits: sendgrid=600/min, twilio=300/min, movistar-ec=120/min, unknown=60/min
- [x] Register webhook validators as keyed services in Program.cs (`"twilio"`, `"sendgrid"`, `"movistar-ec"`)
- [x] Tests pass
- **Spec ref**: Design §8 (webhook rate-limit table, deny-by-default, validation order), Spec all webhook scenarios

---

## Slice 2a — Movistar EC SMS Adapter

**Goal**: `MovistarEcuadorSmsProvider` behind `INotificationProvider`. Wire protocol fully hidden. Degraded-confirmation path if no DLR. Depends on 1b (same provider pattern).

### Task 2a.1 — MovistarEcuadorSmsProvider adapter
- [ ] Write failing tests (HTTP-mocked — SOAP or REST per config):
  - Happy SOAP path: correct `XDocument` POST to Movistar endpoint; parses response; `Accepted`
  - SOAP fault `soap:Server` → `TransientFailure`
  - SOAP fault `soap:Client` → `PermanentFailure`
  - `INVALID_MSISDN`, `BLACKLISTED`, `AUTH_FAILED` → `PermanentFailure`
  - `SYSTEM_BUSY`, `THROTTLED`, `429`, `5xx` → `TransientFailure`
  - Degraded path (no DLR): `SendAsync` returning `Accepted` sets `DeliveredOn` immediately; row → `Sent`
  - `CanHandle(destination)`: returns `true` only for `+593` prefix
  - File: `CardVault.Tests/Features/Notifications/Providers/MovistarEcuadorSmsProviderTests.cs`
- [ ] Create `CardVault.Api/Services/Notifications/Providers/MovistarEcuadorSmsProvider.cs`
  - Wire protocol: SOAP via `XDocument`/`HttpClient` (no WCF); OR REST JSON — controlled by `MovistarOptions.UseRestProtocol`
  - `CanHandle`: `destination.StartsWith("+593")`
  - Degraded: if `MovistarOptions.DegradedConfirmation = true` → on Accepted, set `DeliveredOn = DateTimeOffset.UtcNow`, emit `pci.notification.delivery-confirmed` at send time, log as SBS-evidence limitation
  - Classifies per design §6 Movistar table
- [ ] Create `CardVault.Api/Services/Notifications/Providers/MovistarOptions.cs`
- [ ] Register typed HttpClient in Program.cs
- [ ] Tests pass
- **Spec ref**: Design §13 (Movistar isolation, degraded confirmation), §6 (Movistar classification table)

---

## Slice 2b — TenantNotificationSettings + DB Routing + Admin Endpoint

**Goal**: Per-tenant primary/fallback chain from database. Depends on 1d and 2a.

### Task 2b.1 — TenantNotificationSettingsEntity + EF config + migration
- [ ] Write failing tests:
  - Entity has: `Id (Guid)`, `TenantId (Guid)`, `Channel (NotificationChannel)`, `ProviderId (string max 32)`, `Enabled (bool)`, `Priority (int)`, `CreatedOn (DateTimeOffset)`
  - Unique index `(TenantId, Channel, ProviderId)` exists
  - Index `(TenantId, Channel, Priority)` exists
  - Migration `AddTenantNotificationSettings` has valid `Up` and `Down`
  - File: `CardVault.Tests/Features/Notifications/Persistence/TenantNotificationSettingsEntityTests.cs`
- [ ] Create `CardVault.Infrastructure.Persistence/Notifications/TenantNotificationSettingsEntity.cs`
- [ ] Add EF config and `DbSet<TenantNotificationSettingsEntity>` to `CardVaultDbContext`
- [ ] Run `dotnet ef migrations add AddTenantNotificationSettings`
- [ ] Tests pass
- **Spec ref**: Design §4 (TenantNotificationSettingsEntity), §12 (Slice 2)

### Task 2b.2 — DB-backed NotificationProviderRegistry upgrade
- [ ] Write failing tests:
  - Registry queries `TenantNotificationSettings` ordered by Priority for (tenantId, channel)
  - Filters by `CanHandle(destination)` (Movistar only +593)
  - Appends Twilio as implicit global SMS fallback
  - 5-min cache invalidation
  - Tenant with no settings → `[Twilio]` for SMS, `[SendGrid]` for Email
  - File: `CardVault.Tests/Features/Notifications/Registry/DbBackedNotificationProviderRegistryTests.cs`
- [ ] Upgrade `NotificationProviderRegistry.cs` to query `TenantNotificationSettings` via `CardVaultDbContext` (using `IServiceScopeFactory` from singleton)
- [ ] Tests pass
- **Spec ref**: Design §7 (Per-Tenant Routing), Spec "Per-Tenant Provider Routing" scenarios

### Task 2b.3 — notifications:admin permission + TenantNotificationSettings management endpoint
- [ ] Write failing tests:
  - `GET /api/notifications/tenant-settings?tenantId=` requires `notifications:admin` permission → 403 without it
  - `POST /api/notifications/tenant-settings` creates a setting; duplicate (TenantId,Channel,ProviderId) → 409
  - `DELETE /api/notifications/tenant-settings/{id}` removes a setting
  - File: `CardVault.Tests/Features/Notifications/Webhooks/TenantSettingsEndpointTests.cs` (integration)
- [ ] Add `NotificationsAdmin = "notifications:admin"` to `PermissionCatalog.cs` and `All` list + `Descriptions` dict
- [ ] Add `CanManageNotifications` authorization policy in Program.cs
- [ ] Add 3 actions to `NotificationsController.cs` (or new `TenantNotificationSettingsController.cs`):
  - `GET /api/notifications/tenant-settings` (requires `CanManageNotifications`)
  - `POST /api/notifications/tenant-settings` (requires `CanManageNotifications`)
  - `DELETE /api/notifications/tenant-settings/{id}` (requires `CanManageNotifications`)
- [ ] Tests pass
- **Spec ref**: Design §12 (Slice 2: `notifications:admin` permission + tenant-settings management endpoint), Proposal §Affected Areas

---

## Slice 2c — Metrics + Trace Propagation + Spec Sync + Docs

**Goal**: Full observability. Spec reconciliation. ADR documentation. Depends on 1d, 1e, 2b.

### Task 2c.1 — Prometheus metrics + OpenTelemetry trace spans
- [ ] Write failing tests:
  - `notifications_sent_total{channel,provider,status}` counter increments on each delivery outcome
  - `notifications_retry_total` counter increments on each retry
  - `notifications_provider_latency_ms` histogram records per-provider call duration
  - Trace span created per provider call with `providerReference` in baggage
  - File: `CardVault.Tests/Features/Notifications/Observability/NotificationMetricsTests.cs`
- [ ] Add `Meter("CardVault.Metrics")` counters and histograms in `NotificationDispatcher.cs`:
  - `notifications_sent_total` (labels: channel, provider, status=sent|failed|deadletter)
  - `notifications_retry_total` (labels: channel, provider)
  - `notifications_provider_latency_ms` histogram
- [ ] Wrap each provider call in a child `ActivitySource("CardVault").StartActivity("notification.send.{providerId}")`
- [ ] Write `providerReference` to `Activity.Current?.Baggage` on success
- [ ] Tests pass
- **Spec ref**: Proposal §Audit & Observability (metrics list), Design §14 (trace propagation)

### Task 2c.2 — Spec reconciliation + ADR documentation
- [ ] Update `openspec/specs/customer-notifications/spec.md` (base spec) to merge the delta spec requirements (mark as active, not delta)
- [ ] Ensure the following design corrections are reflected in the merged spec:
  - `Status` stays `Sent` on webhook (not `DeliveryConfirmed`)
  - `Attempts` (not `AttemptCount`)
  - SendGrid uses ECDSA (not HMAC)
  - Dispatcher path is `Background/` (not `Workers/`)
- [ ] Review and update `openspec/changes/real-notification-channels/design.md` to mark Open Questions resolved (Q1 through Q5 per design §6, §7, §8, §13)
- [ ] No test needed; this is a documentation work unit
- **Spec ref**: Design §16 (Risks — "Spec divergences to reconcile in tasks"), Design §15 (ADRs 1-9)

---

## Cross-Slice: Testability Invariants (apply to every slice)

These constraints apply to ALL test code written in this change:

- `FakeNotificationProvider` (Slice 1a.4) is the ONLY provider used in dispatcher unit tests — no real HTTP calls
- `IDeliveryStateMachine` accepts `Func<DateTimeOffset>` clock — inject `() => fixedClock` in tests
- HMAC/ECDSA tests use pre-computed signatures with known secrets — never call real provider validation endpoints
- In-memory EF provider (`UseInMemoryDatabase`) for dispatcher unit tests; `CardVaultWebApplicationFactory` only for webhook integration tests
- `RealProvidersEnabled = false` default in all tests; set to `true` only in tests that explicitly test the enabled path
- Provider outcome queue in `FakeNotificationProvider` dequeues per call — enables multi-step fault injection (e.g., Transient, Transient, Accepted for retry-then-Sent proof)

---

## Dependency Graph

```
1a (abstraction + FSM + fake + registry stub)
  |
  +--- 1b (SendGrid + Twilio adapters) ← EMAIL UNBLOCKED HERE
  |         |
  |         +--- 1e (webhooks + rate-limit)
  |
  +--- 1c (Razor templates + PCI guard)
  |
  +--- 1d (dispatcher rewrite + migration + retry/backoff)
        |       \
        |        +--- 1e (webhooks)
        |
        +--- 2a (Movistar EC adapter)
                |
                +--- 2b (per-tenant settings + DB routing + admin endpoint)
                          |
                          +--- 2c (metrics + trace + spec sync)
```

Sequential constraints:
- 1b depends on 1a (needs interfaces and registry)
- 1c depends on 1a (needs TemplateModel, renderer interface)
- 1d depends on 1a + 1b + 1c (needs providers, renderer, FSM)
- 1e depends on 1b (needs provider options/secrets) and 1d (needs DeliveredOn path)
- 2a depends on 1b (same provider pattern)
- 2b depends on 1d (needs dispatcher to route) and 2a (Movistar must exist to configure)
- 2c depends on 1d + 1e + 2b (metrics/traces span all runtime paths)

Can run in parallel: **1b and 1c** (both depend only on 1a, no mutual dependency); **2a** can start as soon as 1b is done.

---

## Review Workload Forecast

| Metric | Value |
|--------|-------|
| Total estimated prod lines | ~2,280 |
| Total estimated test lines | ~1,880 |
| Total estimated changed lines | ~4,160 |
| 400-line budget risk | HIGH |
| Chained PRs recommended | Yes |
| Decision needed before apply | Yes (already resolved: `auto-chain`) |
| Chain strategy | `stacked-to-main` (each slice lands independently to main in order) |
| Slices | 8 (1a, 1b, 1c, 1d, 1e, 2a, 2b, 2c) |
| Estimated PR count | 8 |
| EMAIL dependency unblocked at | Slice 1b (PR #2 in sequence: after 1a) |

**Bottleneck note**: Slice 1d is the longest single slice (~730 lines combined) and sits on the critical path. If 1d is further split, natural seam is task 1d.2 (dispatcher core) as its own PR and 1d.3+1d.4 as a follow-on.
