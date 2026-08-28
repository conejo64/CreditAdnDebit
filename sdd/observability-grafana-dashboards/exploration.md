## Exploration: observability-grafana-dashboards

### Current State
The infrastructure currently defined in `backend/deploy/docker-compose.yml` includes only the database/messaging persistence layer (PostgreSQL, SQL Server, Kafka) and the .NET application services (CardVault, IsoSwitch, IsoAudit). There are no observability components such as Prometheus, OpenTelemetry Collector, or Grafana present. Metrics are not currently being scraped or visualized at the infrastructure level.

### Affected Areas
- `backend/deploy/docker-compose.yml` — Will need to be updated to include new services for the observability stack (Grafana, Prometheus, and OpenTelemetry Collector).
- `backend/deploy/grafana/provisioning/dashboards/` — New directories and files will be needed to automatically provision the ISO8583 switch metrics dashboard (`isoswitch.json`) and the dashboard provider configuration.
- `backend/deploy/grafana/provisioning/datasources/` — Will need a configuration file to automatically provision Prometheus as a data source for Grafana.
- `backend/deploy/otel/` and `backend/deploy/prometheus/` - Configuration files for the collector and prometheus scraper.

### Approaches
1. **Prometheus + Grafana (Direct Scrape)** — Services expose a `/metrics` endpoint directly, Prometheus scrapes it, and Grafana visualizes it.
   - Pros: Simpler architecture, fewer moving parts.
   - Cons: Less flexible if we want to add tracing or logging later.
   - Effort: Low

2. **OpenTelemetry Collector + Prometheus + Grafana** — Services push OTLP metrics to the Collector, which exports them to Prometheus (or Prometheus scrapes the collector), and Grafana visualizes them.
   - Pros: Future-proof for tracing and logging, centralized configuration for telemetry pipeline, decoupled from Prometheus. Standard for .NET OpenTelemetry implementations.
   - Cons: Slightly more complex infrastructure (adds another component).
   - Effort: Medium

### Recommendation
Option 2 (OpenTelemetry Collector + Prometheus + Grafana) is recommended. Since this is an ASP.NET Core / .NET 9 microservices environment, leveraging the OpenTelemetry Collector sets a solid foundation not just for metrics (TPS, latency, success rate) but for future distributed tracing across Kafka and HTTP boundaries. Grafana can be configured via provisioning to automatically load the `isoswitch.json` dashboard on startup without manual UI interaction.

### Risks
- Adding observability components (Grafana, Prometheus, OTel Collector) will increase the local memory and CPU footprint of the `docker-compose.yml` stack.
- The .NET applications must already be configured (or will need to be configured) to export metrics to the chosen telemetry sink.
- Port collisions for Grafana (3000), Prometheus (9090), and OTel (4317/4318/8888) if these ports are already in use on the host.

### Ready for Proposal
Yes — The orchestrator should proceed with proposing the concrete compose service definitions and the Grafana provisioning layout.
