## ADDED Requirements

### Requirement: Portfolio Analytics Dashboard
The system SHALL expose aggregated issuer portfolio KPIs for internal analytics users.

#### Scenario: Portfolio overview is requested
- WHEN an authorized internal user requests the analytics dashboard
- THEN CardVault returns aggregated counts and balances such as customers, accounts, active cards, portfolio exposure, and open dispute indicators

### Requirement: Consumption Analytics
The system SHALL provide graph-ready consumption analytics grouped by supported business dimensions.

#### Scenario: Consumption report is requested
- WHEN an authorized internal user requests the consumption analytics report
- THEN CardVault groups recent financial activity by supported transaction category, product, and date
- AND CardVault returns totals and trend series suitable for dashboard charts

### Requirement: Fraud Trend Analytics
The system SHALL provide aggregated fraud and dispute trend reporting.

#### Scenario: Fraud report is requested
- WHEN an authorized internal user requests the fraud analytics report
- THEN CardVault returns dispute counts, exposure amounts, trend series, and breakdowns by network and reason code

### Requirement: PCI-Safe Analytics Exposure
The system MUST expose analytics without leaking PCI-sensitive values.

#### Scenario: Analytics data is returned
- WHEN CardVault serves analytics responses
- THEN the response contains only aggregated operational data
- AND CardVault does not expose raw PAN, token payloads, or personally identifying details beyond internal safe identifiers already authorized elsewhere

### Requirement: Analytics Access Auditability
The system MUST preserve traceability for analytics report access.

#### Scenario: Analytics report is viewed
- WHEN an authorized user accesses an analytics endpoint
- THEN CardVault records an audit event with the report type, requested window, and trace context
