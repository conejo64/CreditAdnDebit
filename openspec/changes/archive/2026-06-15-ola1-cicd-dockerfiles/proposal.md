# Proposal: Ola 1 — CI Pipeline, Dockerfiles & Deployable Builds

## Intent

Ola 0 closed the security holes; the platform is now safe but still a prototype in the literal sense: there is **zero CI**, two of three services have **no Dockerfile**, the one Dockerfile that exists is **broken** (stale `cardswitch_solution/` path), and the Angular app cannot produce a deployable bundle (no `environment.prod.ts`, no `fileReplacements`, every URL hardcoded to `localhost`). Nobody can prove a green build on a clean machine, and nobody can ship an image. This is Gate 1 of the commercialization roadmap — "stop being a prototype" — for a card credit/debit platform aimed at Ecuadorian cooperatives/fintechs.

This change establishes the build/packaging foundation: a green GitHub Actions pipeline on every push to `main`, working container images for the three backend services, and an Angular production build that is **not** wired to `localhost`. It does NOT deploy anything to a cloud, provision infrastructure, or refactor application code.

**Success looks like**:
- Every push/PR to `main` runs restore → build → test for the full `.NET` solution (650+ tests green) and `npm ci` + production build for the frontend, on a clean GitHub-hosted runner.
- `docker build` succeeds for CardVault, IsoSwitch, and IsoAudit (built with `backend/` as context so the shared `BuildingBlocks` project resolves).
- `ng build --configuration production` produces a bundle whose API URLs come from `environment.prod.ts` (templatable / non-`localhost`), via `fileReplacements`.
- The `docker-compose` infrastructure stack actually starts (correct Kafka image, correct inter-container listener).

## Scope

### In Scope

- **GitHub Actions CI workflow** (`.github/workflows/ci.yml`, greenfield — none exist today). Single workflow, sequential jobs with `needs:` chaining:
  1. `build-test` — `actions/setup-dotnet@v4` pinned to `9.0.x`; `dotnet restore` → `dotnet build -c Release --no-restore` → `dotnet test -c Release --no-build` against `backend/CardSwitchPlatform.sln`. Uploads the `.trx` test results. **No service-containers** — test factories use EF InMemory + NSubstitute and suppress all hosted services, so no real Postgres/Kafka/SQL Server is needed.
  2. `build-frontend` — `actions/setup-node@v4`, `npm ci --prefix frontend`, production build (`ng build --configuration production`).
  3. `docker-build` (gated on `build-test`) — `docker build` smoke for the three service images to prove the Dockerfiles compile (build only, no push).
- **Three per-service Dockerfiles** — multi-stage (`sdk:9.0` build → `aspnet:9.0` runtime), all built with **`backend/` as the build context** so `shared/BuildingBlocks` is COPY-able. Targeted `COPY` of each `.csproj` before `restore` for NuGet layer caching, then full source copy and `publish`:
  - `IsoAudit.Api/Dockerfile` — **rewrite** (fix stale `cardswitch_solution/` path, add layer caching, COPY shared project, expose `5301`).
  - `CardVault.Api/Dockerfile` — **new** (expose `5101`).
  - `IsoSwitch.Api/Dockerfile` — **new** (expose `5201`).
- **`.dockerignore` files** — none exist anywhere; add them so build contexts exclude `bin/`, `obj/`, `node_modules/`, `.git/`, etc. (frontend `node_modules` alone is hundreds of MB).
- **Frontend production environment** — create `frontend/src/environments/environment.prod.ts` (`production: true`, non-`localhost` / templatable API URLs) and add the `fileReplacements` block to the `production` configuration in `angular.json`.
- **`docker-compose.yml` infrastructure fixes** (`backend/deploy/docker-compose.yml`):
  - Kafka image typo `bitnamilegacy/kafka:3.7` → `bitnami/kafka:3.7`.
  - Advertised listener `PLAINTEXT://localhost:9092` → `PLAINTEXT://kafka:9092` so inter-container traffic resolves.
- **Service containers in compose** — add CardVault / IsoSwitch / IsoAudit (and optionally frontend) service definitions to compose, wiring each service's required env vars (connection strings, `Kafka:BootstrapServers=kafka:9092`, and the JWT secret under the correct key — see asymmetry note). This is the runtime-wiring deliverable that makes the built images usable locally. **Scoped as compose-only**; no orchestrator/cloud target.

### Out of Scope

- **CD / deployment** — no push to a registry (GHCR/ECR), no deploy to any cloud, k8s, or VM. Build-only.
- **HSM / real key management** — secrets remain env/user-secrets injected; no Vault server, no HSM integration.
- **Clean Architecture / structural refactor** — separate change. No application-code changes beyond the frontend environment files.
- **Reconciling the `Jwt:Key` vs `Jwt:SigningKey` config-key asymmetry in code** — IsoAudit reads `Jwt:Key`, the other two read `Jwt:SigningKey`. This change **documents and correctly maps** the env vars per service; it does NOT unify the code keys (that touches Ola 0 territory and is a code change).
- **Testcontainers / real-DB integration tests in CI** — keep InMemory; a migration smoke job is a later, optional addition.
- **Runtime frontend config** (`assets/config.json` + `APP_INITIALIZER`) — build-time `environment.prod.ts` is sufficient for Gate 1; runtime injection is deferred.
- **Multi-environment matrix / parallel Docker matrix builds** — three services do not justify matrix complexity yet.
- **Observability stack changes** — `docker-compose.observability.yml` (Jaeger/Prometheus) is already functional; untouched.

## Capabilities

### New Capabilities

None at the application/spec level — this change adds build/packaging infrastructure, not product behavior.

### Modified Capabilities

None. All deliverables are CI config, Dockerfiles, `.dockerignore`, compose YAML, `angular.json` config, and a new frontend environment file. The `sdd-spec` phase will express these as build/packaging requirements (e.g. "the CI workflow SHALL run the full test suite on every push to main") rather than as changes to existing product specs.

## Approach

Greenfield CI plus per-service packaging, following the `dotnet-core-expert` cloud-native guidance (multi-stage builds, layer-cached restore, ASPNETCORE_URLS, EXPOSE, health-check-ready images).

- **CI**: one `ci.yml`, `build-test` first because it has the highest signal and is fastest (~30–60s, InMemory). `build-frontend` runs in parallel. `docker-build` depends on `build-test` so we never build images for a red solution. Pin `dotnet-version: 9.0.x` (all 13 projects target `net9.0`) and a fixed Node major matching the frontend.
- **Dockerfiles**: identical multi-stage skeleton per service:
  1. `FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build`, set `WORKDIR /src`.
  2. COPY only the `.csproj` files (service projects + `shared/BuildingBlocks`) and `dotnet restore` the service `.csproj` — gives a cached restore layer.
  3. COPY the rest of `backend/` and `dotnet publish -c Release -o /app/publish`.
  4. `FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final`, COPY publish output, `ENV ASPNETCORE_URLS`, `EXPOSE` the service port, `ENTRYPOINT`.
  Build context is `backend/` for all three; document the exact `docker build -f <path> backend/` invocation in tasks.
- **`.dockerignore`**: one at the `backend/` context root (and one for the frontend if a frontend image is added later) excluding `**/bin`, `**/obj`, `**/node_modules`, `.git`, `**/*.user`.
- **Frontend**: add `environment.prod.ts` and the `fileReplacements` mapping `environment.ts → environment.prod.ts` under `angular.json` → `configurations.production`. Verify `ng build --configuration production` emits a bundle referencing the prod URLs.
- **Compose**: fix the two Kafka defects; add the three service containers with env-var blocks. Each service's JWT secret is injected under its actual config key (`Jwt:Key` for IsoAudit, `Jwt:SigningKey` for CardVault/IsoSwitch) using ASP.NET's `__` env-var nesting (e.g. `Jwt__SigningKey`). CardVault gets BOTH `ConnectionStrings__Postgres` and `ConnectionStrings__SqlServerIdentity`. `Kafka__BootstrapServers=kafka:9092` everywhere.

No TDD red/green on the .NET side (no production code changes); verification is "the pipeline is green" and "the images build". The frontend env files have no unit test — verification is a successful prod build with non-`localhost` URLs.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `.github/workflows/ci.yml` | New | Greenfield CI: build-test, build-frontend, docker-build |
| `backend/services/IsoAudit/src/IsoAudit.Api/Dockerfile` | Rewrite | Fix stale `cardswitch_solution/` path; multi-stage + layer cache; context `backend/` |
| `backend/services/CardVault/src/CardVault.Api/Dockerfile` | New | Multi-stage image, port 5101 |
| `backend/services/IsoSwitch/src/IsoSwitch.Api/Dockerfile` | New | Multi-stage image, port 5201 |
| `backend/.dockerignore` (and frontend if needed) | New | Exclude bin/obj/node_modules/.git from contexts |
| `frontend/src/environments/environment.prod.ts` | New | `production: true`, non-localhost API URLs |
| `frontend/angular.json` | Modified | Add `fileReplacements` to production configuration |
| `backend/deploy/docker-compose.yml` | Modified | Fix Kafka image + advertised listener; add 3 service containers |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Docker build context confusion — building from service dir fails because `BuildingBlocks` is outside it | High | All three Dockerfiles built with `backend/` as context; document exact `docker build -f ... backend/` command; CI `docker-build` job proves it |
| JWT config-key asymmetry (`Jwt:Key` vs `Jwt:SigningKey`) misconfigured in compose env | High | Map each service's secret to its real key via `__` nesting; comment the asymmetry inline in compose |
| Ola 0 fail-fast: containers refuse to boot without secrets, breaking `docker compose up` | Medium | Compose injects all required env vars (JWT, both CardVault connection strings, tokenization secret); provide `.env.example` |
| CardVault needs Postgres AND SQL Server — extra ~1.5GB RAM | Medium | Compose-only concern (not CI — tests are InMemory); document memory requirement; runner has ~7GB |
| Frontend prod build still references localhost if `fileReplacements` is wrong | Medium | Verify the emitted bundle's API URLs in the build step; keep URLs templatable |
| Node/`.NET` version drift between CI and dev | Low | Pin `9.0.x` and a fixed Node major in the workflow |
| `bitnami/kafka:3.7` tag unavailable / advertised-listener change breaks an existing local setup | Low | Tag verified valid; `kafka:9092` is the correct intra-network address; document the change |

## Rollback Plan

Every deliverable is additive infrastructure with no application-code coupling, so rollback is low-risk:
- CI workflow: delete/disable `ci.yml` — no runtime impact.
- Dockerfiles / `.dockerignore`: revert the file; old behavior was "no image" or "broken image", so reverting cannot regress a working deployment.
- `angular.json` / `environment.prod.ts`: revert the two-file change; dev build is unaffected.
- `docker-compose.yml`: the Kafka and service-container edits can be reverted independently; the prior compose only ran infra (broken Kafka tag), so reverting returns to the prior broken-but-known state.

Each item is an isolated commit; revert the offending commit. No data migration, no security-tightening rollback constraints.

## Dependencies

- Ola 0 is merged (services fail fast without secrets) — this change must inject those secrets in compose, so it depends on Ola 0's options/validation being in place.
- GitHub Actions enabled on the repo (standard for GitHub-hosted repos).
- No external/infra provisioning, no acquirer/cert coordination, no cloud account.

## Suggested Slicing (auto-chain, ~400-line budget each)

1. **CI workflow** (`ci.yml`) — build-test + build-frontend jobs. Highest signal, no Docker dependency; lands green first.
2. **`.dockerignore` + three Dockerfiles** (rewrite IsoAudit, new CardVault/IsoSwitch) — add `docker-build` job to CI to prove them.
3. **Frontend prod build** (`environment.prod.ts` + `angular.json` fileReplacements) — independent, small.
4. **Compose fixes + service containers** (`docker-compose.yml`) — Kafka image/listener fixes plus the three service definitions with env wiring.

## Success Criteria

- [ ] A push/PR to `main` triggers `ci.yml`; `build-test` runs restore/build/test on `backend/CardSwitchPlatform.sln` and is green (650+ tests).
- [ ] `build-frontend` runs `npm ci` + production build successfully on a clean runner.
- [ ] `docker build -f <service>/Dockerfile backend/` succeeds for CardVault, IsoSwitch, and IsoAudit (CI `docker-build` job green).
- [ ] No Dockerfile references the stale `cardswitch_solution/` path; all use `backend/` as context and COPY the shared `BuildingBlocks` project.
- [ ] A `.dockerignore` excludes `bin/`, `obj/`, `node_modules/`, and `.git/` from build contexts.
- [ ] `ng build --configuration production` produces a bundle whose API URLs come from `environment.prod.ts` (no `localhost`), confirmed via `fileReplacements`.
- [ ] `docker-compose.yml` uses `bitnami/kafka:3.7` and advertises `PLAINTEXT://kafka:9092`.
- [ ] `docker compose up` starts the three services with all required secrets/env injected (JWT under the correct per-service key, CardVault's two connection strings, `Kafka:BootstrapServers=kafka:9092`) without fail-fast boot errors.
