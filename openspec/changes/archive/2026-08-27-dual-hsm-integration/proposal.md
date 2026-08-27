## Intent

Replace the existing `IMacService` with a more generic `IHsmService` using a Ports & Adapters architecture to support both local development and hardware HSM integration (Thales payShield TCP), enabling proper dual HSM environments.

## Scope

### In Scope
- Define `IHsmService` interface.
- Implement `SoftHsmProvider` for local development.
- Implement `HardwareHsmProvider` for Thales payShield TCP integration.
- Conditionally register providers in `Program.cs` based on `appsettings.json`.
- Strict TDD: Implement tests for command formatting and parsing before implementation.

### Out of Scope
- Implementation of non-MAC/PIN cryptographic functions.
- Cloud HSM integrations (e.g., AWS CloudHSM, Azure Key Vault).

## Capabilities

### New Capabilities
- `hsm-cryptography`: Defines the generic HSM behaviors for MAC/PIN operations across different providers.

### Modified Capabilities
- `vault-and-pci`: Update to use the new `IHsmService` instead of the legacy `IMacService`.

## Approach

Adopt a Ports & Adapters architecture. Define `IHsmService` (Port) and implement `SoftHsmProvider` and `HardwareHsmProvider` (Adapters). The active provider will be resolved at runtime using dependency injection configured via `appsettings.json`. TDD will be strictly enforced, starting with unit tests for Thales TCP message formatting and parsing.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `ZitronSystem/Interfaces/` | New/Modified | Introduce `IHsmService`, deprecate/remove `IMacService`. |
| `ZitronSystem/Providers/` | New | Add `SoftHsmProvider` and `HardwareHsmProvider`. |
| `ZitronSystem/Program.cs` | Modified | Update DI container registration logic. |
| `ZitronSystem/Configuration/` | Modified | Add HSM provider configuration settings. |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| TCP connection failures with Thales HSM | Med | Implement robust retry mechanisms and circuit breakers. |
| Misconfiguration in production | Low | Fail-fast on startup if configuration is missing or invalid. |

## Rollback Plan

Revert the DI registration in `Program.cs` to use the legacy `IMacService` implementation and rollback the `appsettings.json` configuration changes.

## Dependencies

- Thales payShield test environment for hardware provider validation.
- `appsettings.json` for environment-specific configuration.

## Success Criteria

- [ ] `IHsmService` successfully abstracts MAC/PIN operations.
- [ ] Local development can run using `SoftHsmProvider` without hardware.
- [ ] `HardwareHsmProvider` successfully communicates with Thales payShield via TCP.
- [ ] All unit tests for command formatting and parsing pass before implementation.
