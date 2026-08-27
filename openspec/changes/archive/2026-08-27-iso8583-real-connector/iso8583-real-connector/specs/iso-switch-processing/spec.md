</Agent System Instructions>
<Delta for iso-switch-processing>
## MODIFIED Requirements

### Requirement: ISO 8583 Transaction Handling

The system SHALL expose switch APIs that build, send, parse, and persist ISO 8583 transaction activity, routing operational traffic through real acquirer network connectors (e.g., `BanredConnector`, `DatafastConnector`) rather than simulators.
(Previously: The system SHALL expose switch APIs that build, send, parse, and persist ISO 8583 transaction activity.)

#### Scenario: Authorization requests become tracked transactions

- WHEN a caller submits an authorization request
- THEN IsoSwitch builds the ISO message, persists the transaction state, sends the message through the selected real acquirer network connector (e.g., `BanredConnector` or `DatafastConnector`), and records the response outcome

#### Scenario: Utility endpoints support diagnostics

- WHEN an operator uses ISO build or parse utilities
- THEN IsoSwitch returns diagnostic message representations without bypassing the platform’s formatting rules
</Delta for iso-switch-processing>
