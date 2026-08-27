## Implementation Progress
**Change**: infra-docker-setup

### Completed Tasks
- [x] 1.1 Add `healthcheck` to `postgres` service using `pg_isready`.
- [x] 1.2 Add `healthcheck` to `sqlserver` service.
- [x] 2.1 Update Kafka listener configurations for internal/external networking.
- [x] 2.2 Add `healthcheck` to `kafka` service to verify connectivity.
- [x] 3.1 Update `cardvault`, `isoswitch`, and `isoaudit` services to use `depends_on` with `condition: service_healthy` for their dependencies.
- [x] 3.2 Add `restart: unless-stopped` policy to all services.
- [x] 4.1 Create `backend/deploy/.env.example` template for the compose setup.

### Files Changed
- `backend/deploy/docker-compose.yml`
- `backend/deploy/.env.example` (verified existing or created)
- `openspec/changes/infra-docker-setup/tasks.md`

### Status
7/7 tasks complete. Ready for verify.
