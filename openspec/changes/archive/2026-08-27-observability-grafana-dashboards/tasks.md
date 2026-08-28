<Tasks: observability-grafana-dashboards>
## Review Workload Forecast
Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Low

## Phase 1: Configuration & Provisioning
- [x] 1.1 Create Prometheus configuration (`backend/deploy/prometheus/prometheus.yml`) to scrape application `/metrics`.
- [x] 1.2 Create OTel Collector configuration (`backend/deploy/otel/otel-collector-config.yml`) for metric/trace pipelines.
- [x] 1.3 Create Grafana data source provisioning file (`backend/deploy/grafana/provisioning/datasources/datasource.yml`) configured for Prometheus.
- [x] 1.4 Create Grafana dashboard provisioning file (`backend/deploy/grafana/provisioning/dashboards/dashboard.yml`).
- [x] 1.5 Create base IsoSwitch dashboard JSON (`backend/deploy/grafana/dashboards/isoswitch-base.json`) for TPS and Latency.

## Phase 2: Docker Compose Integration
- [x] 2.1 Add OpenTelemetry Collector service to `backend/deploy/docker-compose.yml` with memory limits.
- [x] 2.2 Add Prometheus service to `backend/deploy/docker-compose.yml` with memory limits and volumes for configs.
- [x] 2.3 Add Grafana service to `backend/deploy/docker-compose.yml` with memory limits and volumes for provisioning/dashboards.
- [x] 2.4 Verify network bindings between the backend app, OTel collector, Prometheus, and Grafana.

## Phase 3: Validation
- [x] 3.1 Verify `docker-compose up` starts observability stack cleanly.
- [x] 3.2 Verify Prometheus can successfully scrape backend targets.
- [x] 3.3 Verify Grafana loads default data sources and the IsoSwitch base dashboard automatically.
</Tasks: observability-grafana-dashboards>
