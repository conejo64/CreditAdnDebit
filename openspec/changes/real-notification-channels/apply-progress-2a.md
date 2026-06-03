# Apply Progress — Slice 2a: Movistar EC SMS Adapter

**Change**: `real-notification-channels`  
**Slice**: 2a  
**Mode**: Standard (no Strict TDD module loaded — project uses `strict_tdd: false`)  
**Date**: 2026-06-03

---

## Completed Tasks

- [x] 2a.1 — `MovistarEcuadorSmsProvider` adapter: 30 tests (RED→GREEN confirmed), full SOAP+REST paths, degraded confirmation, `CanHandle`, config, DI registration, dispatcher degraded-path hook.

---

## Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Providers/MovistarEcuadorSmsProvider.cs` | Created | Full `INotificationProvider` implementation for Movistar Ecuador. SOAP and REST paths behind `UseRestProtocol` flag. `IMovistarApiKeyProvider` interface + `EnvironmentMovistarApiKeyProvider`. Degraded confirmation sets `ProviderReportedAt`. |
| `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Providers/MovistarOptions.cs` | Created | Non-secret config (`SenderId`, `UseRestProtocol`, `DegradedConfirmation`, `SoapEndpointPath`, `RestEndpointPath`, `SoapNamespace`, `BaseUrl`). Bound from `Notifications:Providers:MovistarEc`. |
| `backend/services/CardVault/tests/CardVault.Tests/Features/Notifications/Providers/MovistarEcuadorSmsProviderTests.cs` | Created | 30 tests covering: happy SOAP/REST, SOAP fault (Server/Client), error codes (INVALID_MSISDN, BLACKLISTED, AUTH_FAILED, SYSTEM_BUSY, THROTTLED), 429, 5xx, timeout, degraded confirmation, `CanHandle` +593 filter. |
| `backend/services/CardVault/src/CardVault.Api/Program.cs` | Modified | Registered `MovistarOptions`, `IMovistarApiKeyProvider`, `MovistarEcuadorSmsProvider` typed HttpClient, and `INotificationProvider` singleton — BEFORE Twilio (DI order = chain priority). |
| `backend/services/CardVault/src/CardVault.Api/appsettings.json` | Modified | Added `MovistarEc` section under `Notifications:Providers` with all non-secret config defaults. |
| `backend/services/CardVault/src/CardVault.Api/Services/Notifications/NotificationDispatcher.cs` | Modified | Added `PciDeliveryConfirmed` constant. In `Accepted` case: if `result.ProviderReportedAt.HasValue` → set `delivery.DeliveredOn`, call `EmitDeliveryConfirmedEventAsync`. Added `EmitDeliveryConfirmedEventAsync` method (emits `pci.notification.delivery-confirmed` with `degradedConfirmation: true`). |

---

## TDD Cycle Evidence

| Task | RED | GREEN | REFACTOR |
|------|-----|-------|----------|
| 2a.1 — `MovistarEcuadorSmsProvider` | ✅ Build failed `CS0246` (`MovistarEcuadorSmsProvider` not found) | ✅ 30 tests pass | ✅ Additive dispatcher change — 554 CardVault + 37 IsoSwitch pass |

---

## Deviations from Design

None — implementation matches design exactly:
- SOAP/REST protocol behind `UseRestProtocol` flag (Design §13)
- `CanHandle` limited to `+593` prefix (Design §13)
- Error code priority: HTTP 429 → Transient; 5xx → Transient; detail `errorCode` → Permanent/Transient; `soap:Server` → Transient; `soap:Client` → Permanent (Design §6)
- `DegradedConfirmation` sets `ProviderReportedAt` on Accepted; dispatcher hooks `ProviderReportedAt` to set `DeliveredOn` and emit `pci.notification.delivery-confirmed` (Design §13, Spec §6)
- Movistar registered BEFORE Twilio in DI — `NotificationProviderRegistry` inherits priority from DI order (Design §7)

---

## Issues Found

None.

---

## Remaining Tasks in Slice 2a

All tasks in Slice 2a are complete.

---

## Workload / PR Boundary

- **Mode**: Chained PR slice (auto-chain strategy)
- **Current work unit**: Slice 2a — Movistar EC SMS Adapter
- **Chain strategy**: `stacked-to-main` (this PR targets `main` after Slice 1e merged)
- **Boundary**: Everything in this apply batch (3 new files + 3 modified files)
- **Estimated review budget impact**: ~220 prod lines + ~180 test lines ≈ 400 lines (within single-PR budget for this slice)

---

## Status

**1/1 tasks complete. Ready for verify.**
