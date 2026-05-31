# Apply Progress — real-notification-channels Slice 1b
## Updated: 2026-05-31
## Engram: unavailable (file-only backend)

---

## Completed Tasks

### Slice 1a (all done — carried forward from previous run)
- [x] 1a.1 — Core interfaces + value types
- [x] 1a.2 — NotificationDeliveryStatus enum + InvalidDeliveryTransitionException
- [x] 1a.3 — IDeliveryStateMachine implementation + pure unit tests
- [x] 1a.4 — FakeNotificationProvider + FakeProviderRegistry
- [x] 1a.5 — NotificationProviderRegistry (stub — fixed chains)

### Slice 1b (completed this run)
- [x] 1b.1 — NotificationDispatcherOptions + appsettings skeleton + secrets check (8 tests)
- [x] 1b.2 — SendGridEmailProvider adapter (19 tests)
- [x] 1b.3 — TwilioSmsProvider adapter + registry wiring (23 tests)

---

## Test Counts
- Baseline (Slice 1a): 367 CardVault + 37 IsoSwitch
- After Slice 1b: **417 CardVault** + 37 IsoSwitch = +50 new tests
- All green, 0 failures, 0 skips

---

## Files Changed (Slice 1b)

### New production files
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/NotificationDispatcherOptions.cs`
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Providers/TwilioOptions.cs`
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Providers/SendGridOptions.cs`
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Providers/SendGridEmailProvider.cs`
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Providers/TwilioSmsProvider.cs`

### Modified production files
- `backend/services/CardVault/src/CardVault.Api/appsettings.json` — added Notifications section
- `backend/services/CardVault/src/CardVault.Api/Program.cs` — registered options + typed HttpClients

### New test files
- `backend/services/CardVault/tests/CardVault.Tests/Features/Notifications/Config/NotificationConfigTests.cs`
- `backend/services/CardVault/tests/CardVault.Tests/Features/Notifications/Providers/SendGridEmailProviderTests.cs`
- `backend/services/CardVault/tests/CardVault.Tests/Features/Notifications/Providers/TwilioSmsProviderTests.cs`

---

## Simulator Decision
The simulator (`NotificationService.DispatchPendingDeliveriesAsync` lines 92-158) was NOT removed in Slice 1b.
Reason: tasks.md correctly scopes simulator removal to Slice 1d (Task 1d.3). Removing it in 1b without
the replacement `INotificationDispatcher` (1d.2) would leave dispatch in a broken half-state.
The real providers are wired and ready; they will be invoked by the real dispatcher in 1d.
`RealProvidersEnabled = false` by default keeps the system safe until 1d lands.

---

## Remaining Tasks (not yet started)
- [ ] 1c.1 — TemplateModel + PCI pre-render guard
- [ ] 1c.2 — RazorNotificationTemplateRenderer + 10 template files
- [ ] 1d.1 — EF entity delta + migration AddRealNotificationChannels
- [ ] 1d.2 — INotificationDispatcher: claim + FSM + audit events
- [ ] 1d.3 — NotificationDispatcherWorker body swap + simulator removal
- [ ] 1d.4 — Fallback chain accounting unit tests
- [ ] 1e.1 — IWebhookSignatureValidator + per-provider implementations
- [ ] 1e.2 — Webhook endpoint + rate-limit policy + audit events
- [ ] 2a.1 — MovistarEcuadorSmsProvider adapter
- [ ] 2b.1 — TenantNotificationSettingsEntity + EF config + migration
- [ ] 2b.2 — DB-backed NotificationProviderRegistry upgrade
- [ ] 2b.3 — notifications:admin permission + TenantNotificationSettings management endpoint
- [ ] 2c.1 — Prometheus metrics + OpenTelemetry trace spans
- [ ] 2c.2 — Spec reconciliation + ADR documentation
