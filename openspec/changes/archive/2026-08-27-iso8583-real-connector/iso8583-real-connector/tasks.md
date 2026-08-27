<Tasks: iso8583-real-connector>
## Review Workload Forecast
Decision needed before apply: Yes
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Medium

## Phase 1: Foundation
- [x] 1.1 Implement `BanredIso8583Packager` for Banred network message formatting.
- [x] 1.2 Implement `DatafastIso8583Packager` for Datafast network message formatting.

## Phase 2: Core Components
- [x] 2.1 Implement `BanredConnector` extending `IAcquirerConnector` with TCP reconnect and timeout logic.
- [x] 2.2 Implement `DatafastConnector` extending `IAcquirerConnector` with TCP reconnect and timeout logic.
- [x] 2.3 Update `MacService.cs` to integrate HSM-backed MAC generation, failing securely on errors.

## Phase 3: Integration
- [x] 3.1 Update `ConnectorRegistry.cs` to manage `BanredConnector`, `DatafastConnector`, and `SimulatorConnector`.
- [x] 3.2 Update `iso-switch-processing` components to dynamically resolve the active connector based on environment configuration.

## Phase 4: Finalization
- [x] 4.1 Implement configuration toggle (e.g., `appsettings.json` / Env Vars) to select between Real connectors and `SimulatorConnector`.
- [x] 4.2 Verify MAC generation and connection handling gracefully fall back or fail securely.
