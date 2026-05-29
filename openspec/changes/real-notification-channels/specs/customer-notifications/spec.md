# Delta Spec — Real Notification Channels
## Capability: `customer-notifications`
## Change: `real-notification-channels`
## Base spec: `openspec/specs/customer-notifications/spec.md`

---

## Context

The existing `customer-notifications` spec defines notification creation, asynchronous delivery tracking, and history visibility but says nothing about how deliveries are actually dispatched. The current implementation uses a simulator path (`ProviderReference = "sim-..."`) that never reaches a real channel. This delta replaces that implicit silence with explicit SHALL constraints for production-grade delivery.

All requirements below are **additive** (## ADDED) or **narrow changes** to the text of an existing requirement (## MODIFIED). Base requirements not mentioned here remain unchanged.

---

## MODIFIED Requirements

### Requirement: Asynchronous Delivery Tracking

The system SHALL dispatch notification deliveries asynchronously through real external providers (SMS or email), persist every state transition, and retain per-channel status and provider references.

(Rationale: the original requirement was silent on whether dispatch reaches a real provider. This makes explicit that a simulator path does not satisfy the requirement in production.)

#### Scenario: Delivery transitions are persisted on every state change
- GIVEN a `CustomerNotificationDelivery` record exists in state `Pending`
- WHEN the dispatcher worker picks up the record and begins sending
- THEN the record transitions to `Sending` and the new state is persisted before the provider call completes
- AND after a successful provider response the record transitions to `Sent` with a non-null `ProviderReference`
- AND after a failed provider response the record transitions to `Failed` with `LastError` populated

#### Scenario: Simulator references are rejected in production
- GIVEN the feature flag `Notifications:RealProvidersEnabled = true`
- WHEN the dispatcher resolves a provider for any delivery
- THEN no `INotificationProvider` implementation with a `ProviderReference` prefixed `sim-` is invoked
- AND any delivery whose `ProviderReference` matches the pattern `sim-*` that was created before migration is left in its current state and is not reprocessed

---

## ADDED Requirements

### Requirement: Real Provider Integration

The system SHALL deliver notifications through real external providers in production. The `sim-*` simulator path SHALL NOT be invoked when `Notifications:RealProvidersEnabled = true`.

Supported providers for Slice 1: SMS via Twilio (global / international fallback), Email via SendGrid (transactional, with Event Webhook).
Supported providers for Slice 2: SMS via Movistar Ecuador (preferred for `+593` mobile numbers).

#### Scenario: OTP SMS reaches a +593 cardholder via Movistar EC (Slice 2)
- GIVEN a tenant has `MovistarEc` configured as the primary SMS provider
- AND a cardholder has a verified `+593` mobile destination
- WHEN the dispatcher processes an OTP notification delivery
- THEN the dispatcher invokes `MovistarEcuadorSmsProvider` with the correct recipient and payload
- AND the delivery record receives a non-null `ProviderReference` returned by Movistar's gateway
- AND the delivery status transitions to `Sent`

#### Scenario: OTP SMS reaches a +1 cardholder via Twilio (Slice 1)
- GIVEN a tenant's primary SMS provider is Twilio or the tenant has no explicit SMS provider configured
- AND a cardholder has a verified `+1` mobile destination
- WHEN the dispatcher processes an OTP notification delivery
- THEN the dispatcher invokes `TwilioSmsProvider`
- AND the delivery record receives a non-null `ProviderReference` from Twilio's API
- AND the delivery status transitions to `Sent`

#### Scenario: Transactional email reaches a cardholder via SendGrid (Slice 1)
- GIVEN a cardholder has a verified email destination
- WHEN the dispatcher processes a transaction notification delivery for the email channel
- THEN the dispatcher invokes `SendGridEmailProvider` with the appropriate template ID and locale-resolved variables
- AND the delivery record receives a non-null `ProviderReference` (SendGrid message ID)
- AND the delivery status transitions to `Sent`

---

### Requirement: Delivery State Machine

The system SHALL model notification delivery as a finite state machine with states: `Pending`, `Sending`, `Sent`, `Failed`, `DeadLetter`. Transitions SHALL be persisted to the database before any provider call is considered terminal.

Valid transitions: `Pending -> Sending`, `Sending -> Sent`, `Sending -> Failed`, `Failed -> Sending` (retry), `Failed -> DeadLetter`. `DeadLetter` is terminal.

No other transitions are valid. The system SHALL reject any attempt to move a delivery to a state not reachable from its current state.

#### Scenario: Delivery state machine enforces valid transitions only
- GIVEN a `CustomerNotificationDelivery` in state `Sent`
- WHEN any component attempts to transition it to `Sending` or `Pending`
- THEN the system rejects the transition and does not persist the invalid state
- AND an error is logged with the delivery ID, attempted transition, and caller context

#### Scenario: Crash mid-send is recoverable
- GIVEN a delivery is in state `Sending`
- AND the process crashes before receiving a provider response
- WHEN the dispatcher worker restarts
- THEN it reclaims deliveries in `Sending` state whose `SendingStartedOn` timestamp is older than the configured lock TTL (default: 5 minutes)
- AND it resets those deliveries to `Failed` with `LastError = "dispatcher-crash-recovery"` and increments `AttemptCount`
- AND it schedules them for retry according to the backoff policy if `AttemptCount < MaxAttempts`

---

### Requirement: Retry with Exponential Backoff

The system SHALL retry failed deliveries with exponential backoff up to `MaxAttempts = 3` before transitioning to `DeadLetter`. Backoff schedule (jitter +-10% applied): attempt 1->2: 30 seconds; attempt 2->3: 2 minutes; final->DeadLetter: after 10 minutes grace. On permanent `4xx` errors the system SHALL skip remaining retries and transition directly to `DeadLetter`. On `DeadLetter` transition the system SHALL emit `cv.notification.deadletter`.

#### Scenario: Provider 5xx triggers retry with backoff
- GIVEN a delivery is in state `Sending`
- AND the provider returns a `5xx` HTTP error
- WHEN the dispatcher processes the error response
- THEN the delivery transitions to `Failed` with `AttemptCount = 1` and `NextAttemptOn = now + 30s (+-jitter)`
- AND the dispatcher does NOT immediately retry
- AND on the next worker tick after `NextAttemptOn`, the delivery transitions back to `Sending` and the provider is called again

#### Scenario: All attempts exhausted — delivery moves to DeadLetter
- GIVEN a delivery has `AttemptCount = 3` (all attempts used)
- AND the provider returns an error on the final attempt
- WHEN the dispatcher processes the error
- THEN the delivery transitions to `DeadLetter`
- AND the system emits event `cv.notification.deadletter` with `deliveryId`, `channel`, `tenantId`, and `lastError`
- AND no further dispatch attempts are made for this delivery ID

#### Scenario: Provider 4xx permanent failure skips remaining retries
- GIVEN a delivery is in state `Sending`
- AND the provider returns a `4xx` HTTP error indicating permanent failure (e.g., invalid destination)
- WHEN the dispatcher processes the error response
- THEN the delivery transitions directly to `DeadLetter` regardless of remaining attempt budget
- AND the system emits `cv.notification.deadletter`

(Design note: providers returning `429` are transient `4xx` and MUST be treated as retryable — the design phase enumerates provider-specific retryable codes.)

---

### Requirement: Inbound Delivery Callbacks (Webhook)

The system SHALL expose `POST /api/notifications/delivery-callback/{providerId}` where `providerId` is a closed set: `twilio`, `movistar-ec`, `sendgrid`. The system SHALL verify the HMAC signature of every inbound callback using the provider's documented scheme and a secret from the secret store. Unsigned, invalid-signature, or replayed requests SHALL be rejected `HTTP 401` without touching any delivery record. Every rejected callback SHALL produce an audit event. The endpoint SHALL be exempt from JWT authentication and SHALL be rate-limited per `providerId`.

#### Scenario: Valid signed callback updates delivery status
- GIVEN a provider sends a delivery-confirmed callback with a valid HMAC signature
- AND the callback references a known `deliveryId`
- WHEN the endpoint processes the request
- THEN the matching delivery record is updated: `DeliveredOn = provider-reported timestamp` (delivery-confirmed is recorded against the existing `Sent` record, not a new FSM state)
- AND the system emits `pci.notification.delivery-confirmed`
- AND the endpoint returns `HTTP 200`

#### Scenario: Unsigned callback is rejected with 401 and audit event
- GIVEN a callback arrives with no HMAC signature header
- WHEN the endpoint processes the request
- THEN no delivery record is updated
- AND the system emits an audit event with rejection reason `missing-signature`
- AND the endpoint returns `HTTP 401`

#### Scenario: Tampered-signature callback is rejected with 401 and audit event
- GIVEN a callback arrives with an HMAC signature that does not match the computed value
- WHEN the endpoint processes the request
- THEN no delivery record is updated
- AND the system emits an audit event with rejection reason `invalid-signature`
- AND the endpoint returns `HTTP 401`

#### Scenario: Unknown providerId is rejected
- GIVEN a request arrives at `POST /api/notifications/delivery-callback/unknown-provider`
- WHEN the endpoint processes the request
- THEN the system returns `HTTP 404` and does not process the body

---

### Requirement: Per-Tenant Provider Routing

The system SHALL select the notification provider per `(tenantId, channel)` tuple using `TenantNotificationSettings`. Each tenant SHALL configure a primary provider and an ordered fallback chain per channel. If the primary fails after exhausting its retry budget, the dispatcher SHALL attempt the next provider in the chain for the same delivery without resetting the global `AttemptCount` (`MaxAttempts = 3` is shared across the entire provider chain). Twilio SHALL be the implicit global SMS fallback for all tenants if no other provider in the chain succeeds.

#### Scenario: Tenant primary provider succeeds — fallback is not used
- GIVEN tenant A has `MovistarEc` as primary SMS provider
- AND `MovistarEc` responds with success on the first attempt
- WHEN the dispatcher processes an SMS delivery for tenant A
- THEN only `MovistarEc` is invoked
- AND the delivery record's `ProviderId` is set to `movistar-ec`

#### Scenario: Primary provider fails — fallback provider is used
- GIVEN tenant A has `MovistarEc` as primary and `Twilio` as fallback SMS provider
- AND `MovistarEc` returns a 5xx error and exhausts its retry budget
- WHEN the dispatcher moves to the fallback
- THEN `TwilioSmsProvider` is invoked for the same delivery
- AND if Twilio succeeds, the delivery transitions to `Sent` with `ProviderId = twilio`

#### Scenario: Tenant with no explicit SMS config uses Twilio global fallback
- GIVEN a tenant has no entry in `TenantNotificationSettings` for channel `SMS`
- WHEN the dispatcher processes an SMS delivery for that tenant
- THEN the dispatcher routes to `TwilioSmsProvider` by default
- AND the delivery proceeds normally

---

### Requirement: PCI-Safe Templating

The system SHALL render notification content using locale-aware Razor templates. Supported locales: `es-EC` and `en-US`. Locale is selected from `Customer.PreferredLocale`; default is `es-EC`. Required template types: `OTP`, `TransactionNotification`, `SecurityAlert`, `StatementAvailable`, `PaymentReceived`.

Templates SHALL be PCI-safe. The following SHALL NOT appear in any rendered output: raw (unmasked) PAN, OTP secret or seed material, full CVV/CVV2. Permitted forms: last 4 PAN digits (format: `****NNNN`), transaction amount, masked merchant name, timestamp.

#### Scenario: OTP template renders without exposing the secret
- GIVEN a cardholder with `PreferredLocale = es-EC` requests an OTP
- WHEN the template renderer processes the OTP notification payload
- THEN the rendered body contains the OTP code as a one-time value but DOES NOT contain the OTP secret key or any seed material
- AND the rendered body DOES NOT contain the raw PAN

#### Scenario: Transaction notification template renders with masked PAN
- GIVEN a transaction notification payload containing a full PAN for internal reference
- WHEN the template renderer processes the payload
- THEN the rendered output contains only the last 4 digits in format `****NNNN`
- AND the template engine rejects any variable binding supplying an unmasked PAN longer than 4 digits to a renderable field

#### Scenario: Locale fallback applies when PreferredLocale is unavailable
- GIVEN a cardholder has `PreferredLocale = null` or an unsupported locale
- WHEN the template renderer selects a locale
- THEN the renderer defaults to `es-EC`
- AND the rendered content is in Spanish (Ecuador)

---

### Requirement: PCI Audit Events on State Transitions

The system SHALL emit a PCI audit event for every notification delivery state transition through `PciAuditPublisher`.

Required events:
- `pci.notification.send-attempt`: emitted when delivery transitions to `Sending`
- `pci.notification.send-result`: emitted when delivery transitions to `Sent` or `Failed` after a provider call
- `pci.notification.delivery-confirmed`: emitted when a valid signed callback is applied
- `pci.notification.deadletter`: emitted when delivery transitions to `DeadLetter`

Each event SHALL include: `deliveryId`, `notificationId`, `tenantId`, `channel`, `providerId` (if known), `attemptCount`, UTC timestamp. Events SHALL NOT include raw PAN, OTP value, destination address, or message body.

#### Scenario: send-attempt event is emitted when dispatcher picks up a delivery
- GIVEN a delivery in state `Pending`
- WHEN the dispatcher transitions it to `Sending`
- THEN the system emits `pci.notification.send-attempt` before the provider call is made
- AND the event contains `deliveryId`, `tenantId`, `channel`, `attemptCount`, and UTC timestamp
- AND the event does not contain the message body or destination address

#### Scenario: send-result event is emitted after provider call completes
- GIVEN the dispatcher has called the provider for a delivery
- WHEN the provider returns a response (success or failure)
- THEN the system emits `pci.notification.send-result` with `outcome = sent | failed`
- AND the event contains `providerReference` (non-null on success) and `errorCode` (null on success)

#### Scenario: deadletter event is emitted when all attempts are exhausted
- GIVEN a delivery has `AttemptCount = MaxAttempts` and the final attempt fails
- WHEN the dispatcher transitions to `DeadLetter`
- THEN the system emits `pci.notification.deadletter` with `deliveryId`, `lastError`, and `attemptCount`

#### Scenario: delivery-confirmed event is emitted on valid webhook
- GIVEN a provider sends a valid signed delivery callback
- WHEN the webhook endpoint successfully applies the callback
- THEN the system emits `pci.notification.delivery-confirmed` with `deliveryId`, `providerId`, `deliveredOn`, and `tenantId`

---

### Requirement: Secrets and Provider Configuration

Provider API keys and HMAC signing secrets SHALL be read from the runtime secret store (environment variables or vault). Secrets SHALL NOT appear in any committed `appsettings*.json` file. Non-secret config (base URLs, sender IDs, template IDs) is stored under `Notifications:Providers:{Twilio|MovistarEc|SendGrid}`.

#### Scenario: Service starts with provider secrets from environment
- GIVEN `Notifications__Providers__Twilio__AuthToken` is set as an environment variable
- AND no Twilio auth token appears in any committed configuration file
- WHEN CardVault.Api starts
- THEN the Twilio provider initializes successfully using the environment variable value
- AND a grep of committed config files for `SG.` or `AC[0-9a-f]{32}` returns no matches

---

## Slice Sequencing Note

This spec covers the **full contract**. Tasks will sequence:
- **Slice 1**: Twilio (SMS) + SendGrid (Email) + state machine + retry + audit events + HMAC webhooks for Twilio and SendGrid.
- **Slice 2**: Movistar EC (SMS) + per-tenant routing with Movistar as primary + HMAC webhook for Movistar EC.
