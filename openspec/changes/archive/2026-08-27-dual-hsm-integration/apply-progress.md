## Implementation Progress
**Change**: dual-hsm-integration
### Completed Tasks
- [x] 1.1 Create `IHsmService` interface defining MAC generation and PIN block parsing methods.
- [x] 2.1 Write failing test for `SoftHsmProvider` PIN block parsing.
- [x] 2.2 Implement `SoftHsmProvider` PIN block parsing.
- [x] 2.3 Write failing test for `SoftHsmProvider` MAC generation.
- [x] 2.4 Implement `SoftHsmProvider` MAC generation.
- [x] 3.1 Write failing test for Thales TCP command formatting.
- [x] 3.2 Implement Thales TCP command formatting logic.
- [x] 3.3 Write failing test for Thales TCP response parsing.
- [x] 3.4 Implement Thales TCP response parsing logic.
- [x] 3.5 Write failing tests for `HardwareHsmProvider` communication failures and core operations (MAC/PIN).
- [x] 3.6 Implement `HardwareHsmProvider` communication logic, MAC generation, and PIN parsing.
- [x] 4.1 Write failing test for `vault-and-pci` components using `IHsmService`.
- [x] 4.2 Refactor existing services to use `IHsmService`, replacing `IMacService`.
- [x] 4.3 Update `appsettings.json` with HSM provider settings.
- [x] 4.4 Update `Program.cs` to conditionally register the active HSM provider.
- [x] 4.5 Remove legacy `IMacService` and associated unused code.

### Files Changed
- `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/Iso/IHsmService.cs`
- `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/Iso/SoftHsmProvider.cs`
- `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/Iso/HardwareHsmProvider.cs`
- `backend/services/IsoSwitch/tests/IsoSwitch.Tests/Infrastructure/Hsm/SoftHsmProviderTests.cs`
- `backend/services/IsoSwitch/tests/IsoSwitch.Tests/Infrastructure/Hsm/HardwareHsmProviderTests.cs`
- `backend/services/IsoSwitch/src/IsoSwitch.Application/Features/Transactions/Commands/AuthorizeTransaction/AuthorizeTransactionCommandHandler.cs`
- `backend/services/IsoSwitch/src/IsoSwitch.Application/Features/Transactions/Commands/CaptureTransaction/CaptureTransactionCommandHandler.cs`
- `backend/services/IsoSwitch/src/IsoSwitch.Application/Features/Transactions/Commands/ReversalAdvice/ReversalAdviceCommandHandler.cs`
- `backend/services/IsoSwitch/src/IsoSwitch.Application/Features/Transactions/Commands/ReversalTransaction/ReversalTransactionCommandHandler.cs`
- `backend/services/IsoSwitch/src/IsoSwitch.Api/Program.cs`
- `backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.json`
- `openspec/changes/dual-hsm-integration/tasks.md`

### Status
16/16 tasks complete. Ready for verify.

### Corrective Actions Taken
- Deleted legacy `IMacService.cs` and `MacService.cs`.
- Fixed compilation errors in test suite (`AuthorizeTransactionCommandHandlerTests.cs`, `CaptureTransactionCommandHandlerTests.cs`, `ReversalAdviceCommandHandlerTests.cs`, `ReversalTransactionCommandHandlerTests.cs`) by updating `IMacService` dependencies to `IHsmService`.
