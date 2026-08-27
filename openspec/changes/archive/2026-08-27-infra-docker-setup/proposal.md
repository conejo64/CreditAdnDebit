## Intent

Enhance the existing Docker Compose setup to provide a robust, production-ready environment for the ZitronSystem backend services. This ensures reliable startup sequencing and resilience against failures.

## Scope

### In Scope
- Add healthchecks to PostgreSQL and Kafka services in `backend/deploy/docker-compose.yml`.
- Configure .NET services (IsoSwitch, CardVault, IsoAudit) to wait for healthy dependencies (`depends_on` with `condition: service_healthy`).
- Fix Kafka advertised listeners to support both INTERNAL and EXTERNAL networking.
- Apply `restart: unless-stopped` policies across services.
- Create an environment configuration template (`.env.example`).

### Out of Scope
- Orchestration for Kubernetes or Docker Swarm (this focuses purely on single-host Compose).
- Application code refactoring.
- Provisioning external cloud infrastructure.

## Capabilities

### New Capabilities
- `devops-orchestration`: Defines the local and deployment Docker Compose topology, service dependencies, health checks, and restart policies.

### Modified Capabilities
- None

## Approach

We will modify the existing `backend/deploy/docker-compose.yml`. We'll introduce Docker-native healthchecks for the infrastructure services (PostgreSQL, Kafka). We'll update the application services (IsoSwitch, CardVault, IsoAudit) to declare explicit dependencies on the health state of these infrastructure services. Additionally, we will configure robust Kafka listener settings and container restart policies, and ensure environment variables are well-documented via a `.env.example` file.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `backend/deploy/docker-compose.yml` | Modified | Added healthchecks, restart policies, listener configs, and depends_on blocks. |
| `backend/deploy/.env.example` | New | Environment variable template for the compose setup. |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Circular dependencies in startup | Low | Use structured healthchecks and verify the DAG of dependencies. |
| Configuration drift between local and prod | Med | Rely on `.env` files and document the expected variables. |

## Rollback Plan

Revert the changes made to `backend/deploy/docker-compose.yml` via git. Remove the `.env` dependencies and restore the previous generic `depends_on` configuration without healthchecks.

## Dependencies

- Existing `Dockerfile`s for the .NET services must expose necessary ports and be buildable.
- Docker and Docker Compose environment on the target host.

## Success Criteria

- [ ] `docker-compose up -d` successfully starts all services in the correct order.
- [ ] IsoSwitch, CardVault, and IsoAudit wait for PostgreSQL and Kafka to be healthy before starting.
- [ ] Containers automatically restart if they crash.
- [ ] Kafka successfully accepts internal connections from the services and external connections if configured.
