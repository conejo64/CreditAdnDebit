# Apply Progress — Slice 1c
## Change: real-notification-channels
## Completed: 2026-05-31
## Engram: unavailable (file-only artifact store)

---

## Status: DONE

All Slice 1c tasks completed. 449 CardVault tests green (417 baseline + 32 new).

---

## Tasks Completed

### Task 1c.1 — TemplateModel + PCI pre-render guard [x]
- `TemplateModel` sealed record with: `TemplateType`, `Locale`, `MaskedPan`, `Amount`, `CurrencyCode`, `MaskedMerchant`, `Timestamp`, `OtpCode`, `AdditionalData`
- `PciTemplateViolationException` with `FieldName` and `Rule` properties; message does NOT echo the offending value
- `PciTemplateGuard.Validate()` enforces:
  - `\d{6,}` consecutive digits rejects raw PANs (checked after stripping hyphens/spaces to catch `4111-1111-1111-1111` format)
  - OTP seed keyword regex (`otp_secret`, `totp_secret`, `shared_secret`, etc.)
  - `OtpCode` field is EXEMPT from digit check (it IS the display code — 6 digits OK); still blocked by seed keywords
  - Fails CLOSED: any violation throws before render occurs
- 18 tests covering: masked PAN passes, 5-digit safe, null safe, OtpCode display passes, hyphenated PAN blocked, seed keywords blocked, message does not leak value

### Task 1c.2 — RazorNotificationTemplateRenderer + 10 template files [x]
- `INotificationTemplateRenderer` interface with `RenderAsync(TemplateModel, CancellationToken)` returning `RenderedTemplate(Subject, Body)`
- `RazorNotificationTemplateRenderer` using `RazorLight` 2.3.1 `FileSystemProject`
  - Template root: `AppContext.BaseDirectory/Services/Notifications/Templates/`
  - Template key: `{TemplateType}.{locale_underscore}` (RazorLight appends `.cshtml`)
  - Locale negotiation: null/empty → `es-EC`; unsupported (e.g. `fr-FR`) → `es-EC`
  - PCI guard runs BEFORE render; body never logged
  - Static `Create(string? templatesPath = null)` factory for easy test injection
- 10 `.cshtml` template files deployed as `CopyToOutputDirectory=PreserveNewest`:
  - Otp.es_EC, Otp.en_US
  - TransactionNotification.es_EC, TransactionNotification.en_US
  - SecurityAlert.es_EC, SecurityAlert.en_US
  - StatementAvailable.es_EC, StatementAvailable.en_US
  - PaymentReceived.es_EC, PaymentReceived.en_US
- Registered in Program.cs: `PciTemplateGuard` as singleton; `INotificationTemplateRenderer` as scoped
- `RazorCompile Remove` + `Content Update CopyToOutputDirectory=PreserveNewest` in csproj (linter contribution)
- 14 renderer tests: each template type × 2 locales renders, locale fallback (null, fr-FR), PCI guard integration blocks unmasked PAN and OTP secret in renderer

---

## Files Changed

### Production
- `CardVault.Api/CardVault.Api.csproj` — added RazorLight 2.3.1; template copy-to-output config
- `CardVault.Api/Program.cs` — added using; registered PciTemplateGuard + INotificationTemplateRenderer
- `CardVault.Api/Services/Notifications/Templates/TemplateModel.cs` (NEW)
- `CardVault.Api/Services/Notifications/Templates/PciTemplateViolationException.cs` (NEW)
- `CardVault.Api/Services/Notifications/Templates/PciTemplateGuard.cs` (NEW)
- `CardVault.Api/Services/Notifications/Templates/INotificationTemplateRenderer.cs` (NEW)
- `CardVault.Api/Services/Notifications/Templates/RazorNotificationTemplateRenderer.cs` (NEW)
- `CardVault.Api/Services/Notifications/Templates/Otp.es_EC.cshtml` (NEW)
- `CardVault.Api/Services/Notifications/Templates/Otp.en_US.cshtml` (NEW)
- `CardVault.Api/Services/Notifications/Templates/TransactionNotification.es_EC.cshtml` (NEW)
- `CardVault.Api/Services/Notifications/Templates/TransactionNotification.en_US.cshtml` (NEW)
- `CardVault.Api/Services/Notifications/Templates/SecurityAlert.es_EC.cshtml` (NEW)
- `CardVault.Api/Services/Notifications/Templates/SecurityAlert.en_US.cshtml` (NEW)
- `CardVault.Api/Services/Notifications/Templates/StatementAvailable.es_EC.cshtml` (NEW)
- `CardVault.Api/Services/Notifications/Templates/StatementAvailable.en_US.cshtml` (NEW)
- `CardVault.Api/Services/Notifications/Templates/PaymentReceived.es_EC.cshtml` (NEW)
- `CardVault.Api/Services/Notifications/Templates/PaymentReceived.en_US.cshtml` (NEW)

### Tests
- `CardVault.Tests/Features/Notifications/Templates/PciTemplateGuardTests.cs` (NEW — 18 tests)
- `CardVault.Tests/Features/Notifications/Templates/RazorNotificationTemplateRendererTests.cs` (NEW — 14 tests)

### Artifacts
- `openspec/changes/real-notification-channels/tasks.md` — Slice 1c tasks marked [x]
- `openspec/changes/real-notification-channels/apply-progress-1c.md` (this file)

---

## Test Results
- Baseline: 417 green
- After Slice 1c: **449 green** (+32)
- Delta: +32 (18 PCI guard + 14 renderer)
- Failed: 0

---

## PCI Guard Summary

The guard fails CLOSED. The enforcement chain:

1. `PciTemplateGuard.Validate(model)` is called by `RazorNotificationTemplateRenderer.RenderAsync` BEFORE the Razor engine ever sees the model
2. If any string field contains 6+ consecutive digits (raw or separator-stripped), throws `PciTemplateViolationException` identifying the field
3. If any string field contains OTP seed/secret keywords (case-insensitive regex), throws
4. `OtpCode` is exempt from the digit check (display code only) but NOT from seed keyword check
5. Exception message deliberately omits the offending value to prevent log leakage
6. 18 tests prove all acceptance and rejection paths

---

## Key Decisions
- Used underscore in filenames (`es_EC`) instead of hyphen (`es-EC`) since `.` is the separator between TemplateType and locale in the filename, and RazorLight's FileSystemProject strips the `.cshtml` extension to produce the key
- FileSystemProject (not EmbeddedResource) — templates deployed as files for runtime editability
- `AdditionalData` field added to `TemplateModel` beyond the spec minimum to support security alert free-form context (also scanned by PCI guard)
- Hyphenated PAN detection: guard strips `-` and ` ` before re-running digit regex (defense-in-depth)

---

## Notes for Slice 1d
- `INotificationTemplateRenderer` is ready to be injected into `NotificationDispatcher`
- The dispatcher should call `renderer.RenderAsync(model, ct)` and pass `RenderedTemplate.Body` as `RenderedBody` to `NotificationSendRequest`
- `TemplateModel.Locale` should be populated from `Customer.PreferredLocale` (currently not on the entity — Slice 1d implementer should add this or default to `es-EC`)
