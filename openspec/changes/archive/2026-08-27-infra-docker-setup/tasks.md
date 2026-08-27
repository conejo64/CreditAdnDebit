<Tasks: infra-docker-setup>
## Review Workload Forecast
Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: auto-chain
400-line budget risk: Low

## Phase 1: Database Health Checks
- [x] 1.1 Add `healthcheck` to `postgres` service using `pg_isready`.
- [x] 1.2 Add `healthcheck` to `sqlserver` service.

## Phase 2: Kafka Config and Health Checks
- [x] 2.1 Update Kafka listener configurations for internal/external networking.
- [x] 2.2 Add `healthcheck` to `kafka` service to verify connectivity.

## Phase 3: Application Dependencies & Policies
- [x] 3.1 Update `cardvault`, `isoswitch`, and `isoaudit` services to use `depends_on` with `condition: service_healthy` for their dependencies.
- [x] 3.2 Add `restart: unless-stopped` policy to all services.

## Phase 4: Environment Configuration
- [x] 4.1 Create `backend/deploy/.env.example` template for the compose setup.
</Tasks: infra-docker-setup>
