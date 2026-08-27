<Tasks: dual-hsm-integration>
## Review Workload Forecast
Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: auto-chain
400-line budget risk: Medium

## Phase 1: Foundation
- [x] 1.1 Create `IHsmService` interface defining MAC generation and PIN block parsing methods.

## Phase 2: SoftHsmProvider (Local Development) - Strict TDD
- [x] 2.1 Write failing test for `SoftHsmProvider` PIN block parsing.
- [x] 2.2 Implement `SoftHsmProvider` PIN block parsing.
- [x] 2.3 Write failing test for `SoftHsmProvider` MAC generation.
- [x] 2.4 Implement `SoftHsmProvider` MAC generation.

## Phase 3: HardwareHsmProvider (Thales TCP) - Strict TDD
- [x] 3.1 Write failing test for Thales TCP command formatting.
- [x] 3.2 Implement Thales TCP command formatting logic.
- [x] 3.3 Write failing test for Thales TCP response parsing.
- [x] 3.4 Implement Thales TCP response parsing logic.
- [x] 3.5 Write failing tests for `HardwareHsmProvider` communication failures and core operations (MAC/PIN).
- [x] 3.6 Implement `HardwareHsmProvider` communication logic, MAC generation, and PIN parsing.

## Phase 4: Integration and Configuration
- [x] 4.1 Write failing test for `vault-and-pci` components using `IHsmService`.
- [x] 4.2 Refactor existing services to use `IHsmService`, replacing `IMacService`.
- [x] 4.3 Update `appsettings.json` with HSM provider settings.
- [x] 4.4 Update `Program.cs` to conditionally register the active HSM provider.
- [x] 4.5 Remove legacy `IMacService` and associated unused code.
</Tasks: dual-hsm-integration>
