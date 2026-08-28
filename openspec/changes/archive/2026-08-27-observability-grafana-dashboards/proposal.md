<Proposal: observability-grafana-dashboards>
## Intent

We need to add full observability capabilities (metrics and dashboards) to the IsoSwitch backend. This will enable monitoring of key performance indicators such as transactions per second (TPS), latency, and overall system health, improving operational readiness.

## Scope

### In Scope
- Add OpenTelemetry Collector, Prometheus, and Grafana to `backend/deploy/docker-compose.yml`.
- Add Grafana provisioning files for automatic dashboard loading.
- Create a base dashboard JSON for IsoSwitch (TPS, Latency, etc.).
- Add configuration files for OTel and Prometheus.

### Out of Scope
- Adding new custom metrics within the application code itself (relying on existing default metrics for now).
- Setting up external alert managers (PagerDuty, etc.).

## Capabilities

### New Capabilities
- `observability-dashboards`: Covers the integration of OpenTelemetry Collector, Prometheus, and Grafana, including automated provisioning of core operational dashboards for IsoSwitch monitoring.

### Modified Capabilities
- None

## Approach

We will enhance the existing local deployment setup (`backend/deploy/docker-compose.yml`) by introducing OpenTelemetry, Prometheus, and Grafana services. We will configure Grafana to automatically provision a pre-built JSON dashboard representing IsoSwitch metrics.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `backend/deploy/docker-compose.yml` | Modified | Add observability services |
| `backend/deploy/grafana/provisioning/` | New | Grafana provisioning configs |
| `backend/deploy/grafana/dashboards/` | New | Base dashboard JSON |
| `backend/deploy/prometheus/` | New | Prometheus configs |
| `backend/deploy/otel/` | New | OTel Collector configs |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Resource exhaustion in local dev environment | Medium | Set appropriate memory and CPU limits in docker-compose for the observability stack |
| Incorrect metrics scraping | Low | Validate Prometheus targets in development |

## Rollback Plan

Revert the changes to `backend/deploy/docker-compose.yml` and remove the added configuration directories for Grafana, Prometheus, and OTel.

## Dependencies

- Docker and Docker Compose environment

## Success Criteria

- [ ] `docker-compose up` successfully starts OTel, Prometheus, and Grafana alongside the application.
- [ ] Grafana is accessible and automatically loads the IsoSwitch base dashboard.
- [ ] The dashboard displays metrics (e.g., TPS, Latency) sourced from Prometheus/OTel.
