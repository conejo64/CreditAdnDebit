<Proposal: iso8583-real-connector>
## Intent

Replace the `SimulatorConnector` with real ISO8583 TCP packagers and connectors for Ecuador networks (Banred, Datafast) to enable real network connectivity and proper HSM-backed MAC generation.

## Scope

### In Scope
- Create explicitly typed classes (`BanredConnector`, `DatafastConnector`) implementing `IAcquirerConnector`.
- Implement bespoke `BanredIso8583Packager` and `DatafastIso8583Packager`.
- Update `MacService.cs` to integrate proper HSM-backed MAC generation.
- Update `ConnectorRegistry.cs` to register the new connectors.

### Out of Scope
- Full HSM deployment or key rotation (only integrating MAC generation placeholder replacement).
- Modifying the core ISO8583 switch processing logic beyond the connector interfaces.

## Capabilities

### New Capabilities
- None

### Modified Capabilities
- `iso-switch-processing`: Update to utilize real acquirer network connectors (`BanredConnector`, `DatafastConnector`) instead of simulators.
- `vault-and-pci`: Update MAC generation in `MacService` to use proper HSM integration.

## Approach

Use "Dedicated Connectors": Create explicitly typed connector classes wrapping a tailored `TcpIsoClient`. Each connector uses its specific packager (`BanredIso8583Packager` and `DatafastIso8583Packager`). `ConnectorRegistry` is updated to instantiate and manage these real connectors. `MacService` is modified to call actual HSM APIs or libraries instead of generating a placeholder SHA256 MAC.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `src/Infrastructure/Connectors` | New/Modified | New Banred/Datafast connectors and packagers; preserve SimulatorConnector via environment configuration. |
| `src/Infrastructure/Connectors/ConnectorRegistry.cs` | Modified | Register real connectors. |
| `src/Infrastructure/Security/MacService.cs` | Modified | Implement HSM-backed MAC generation. |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Connection failures to Banred/Datafast | Med | Implement robust TCP reconnect and timeout logic in the connectors. |
| HSM integration failure | Med | Ensure proper testing of HSM commands and fallback logging if MAC generation fails. |

## Rollback Plan

Revert `ConnectorRegistry.cs` to use `SimulatorConnector` and revert `MacService.cs` to the placeholder SHA256 MAC generation.

## Dependencies

- Network access to Banred and Datafast test/production environments.
- Access to HSM API/Libraries for MAC generation.

## Success Criteria

- [ ] `BanredConnector` and `DatafastConnector` successfully connect and send/receive ISO8583 messages.
- [ ] MAC generation in `MacService` generates valid MACs verifiable by the networks.
- [ ] `SimulatorConnector` is preserved for local/demo environments, controlled strictly via environment configuration, preventing fake success states in production.
