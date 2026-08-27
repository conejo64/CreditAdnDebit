## Apply Progress for iso8583-real-connector

- Implemented Phase 3: Updated `ConnectorRegistry.cs` to inject `IConfiguration` and use the `Iso:ForceSimulator` flag to dynamically force the use of `SimulatorConnector`.
- Updated all unit tests instantiating `ConnectorRegistry` to include a mocked `IConfiguration`.
- Implemented Phase 4: Added `"ForceSimulator": true` configuration toggle in `appsettings.json` and `appsettings.Development.json` for `IsoSwitch.Api`.
- Verified `MacService.cs` fails securely on HSM failures by throwing `InvalidOperationException` and aborting the transaction.
- All tasks in Phase 3 and 4 are complete.
