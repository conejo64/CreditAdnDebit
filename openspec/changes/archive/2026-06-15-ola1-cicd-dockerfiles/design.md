# Design: Ola 1 — CI Pipeline, Dockerfiles & Deployable Builds
# Change: ola1-cicd-dockerfiles

This is the HOW at architectural level. Four additive infrastructure slices (auto-chain, stacked-to-main), each its own commit/PR. No application code changes (frontend env files are config). Verified against the exploration facts (broken IsoAudit path, `backend/` build context, Kafka compose defects, JWT key asymmetry, fail-fast secrets). All 13 projects target `net9.0`; tests are EF InMemory + NSubstitute with hosted services suppressed → no real infra in CI.

## 1. Technical Approach
One greenfield `ci.yml` (parallel `build-test` + `build-frontend`; `docker-build` gated on `build-test`). Per-service multi-stage Dockerfiles, ALL built with `backend/` as context so `shared/BuildingBlocks` is COPY-able. Build-time Angular env substitution via `environment.prod.ts` + `fileReplacements`. Compose remediation (Kafka fixes) plus three service containers wired with fail-fast secrets via `__` env-var nesting. Verification is "pipeline green" + "images build" + "compose up boots" — no .NET red/green (no production code).

## 2. Architecture Decisions (ADRs)

### ADR-1 — CI topology: single workflow, parallel quality jobs, gated docker (Slice 1 + 2)
**Choice**: One `.github/workflows/ci.yml`, triggers `push`/`pull_request` to `main`. Jobs:
- `build-test` (ubuntu-latest): `setup-dotnet@v4` pinned `9.0.x` → `dotnet restore` → `dotnet build -c Release --no-restore` → `dotnet test -c Release --no-build --logger "trx"` on `backend/CardSwitchPlatform.sln`; uploads `.trx`.
- `build-frontend` (ubuntu-latest, parallel, no `needs`): `setup-node@v4` (Node 20), `npm ci --prefix frontend`, `npm run build --prefix frontend`.
- `docker-build` (`needs: build-test`, added in Slice 2): `docker build -f <service>/Dockerfile backend/` for all three (build-only, no push).
**Caching**: `actions/setup-dotnet` cache for NuGet (`cache: true`, `cache-dependency-path: **/packages.lock.json` if present, else key on `**/*.csproj`); `setup-node` `cache: npm`, `cache-dependency-path: frontend/package-lock.json`. Docker layer cache deferred (build-only, low payoff without registry).
**Gate**: `build-test` + `build-frontend` must pass to merge; `docker-build` gates on `build-test` so we never build images for a red solution.
**Alternatives rejected**: matrix-per-service (3 services do not justify complexity, harder job ordering); service-containers in CI (tests are InMemory — pure overhead). **Rationale**: highest-signal/fastest job first, parallel frontend, Docker last and gated.

### ADR-2 — Per-service multi-stage Dockerfile, `backend/` context, identical skeleton (Slice 2)
**Choice**: One Dockerfile per service, identical skeleton, ALWAYS built from `backend/`:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY shared/BuildingBlocks/BuildingBlocks.csproj shared/BuildingBlocks/
COPY services/<Svc>/src/<Svc>.Api/<Svc>.Api.csproj services/<Svc>/src/<Svc>.Api/
# + each transitive .csproj (Application/Domain/Infrastructure.*) of that service
RUN dotnet restore services/<Svc>/src/<Svc>.Api/<Svc>.Api.csproj
COPY . .
RUN dotnet publish services/<Svc>/src/<Svc>.Api/<Svc>.Api.csproj -c Release -o /app/publish
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:<port>
EXPOSE <port>
ENTRYPOINT ["dotnet","<Svc>.Api.dll"]
```
Ports: CardVault 5101, IsoSwitch 5201, IsoAudit 5301 (HTTP only). IsoAudit is a **rewrite** (fixes the stale `cardswitch_solution/` path). The exact `docker build -f services/<Svc>/src/<Svc>.Api/Dockerfile backend/` invocation is documented in tasks and exercised by the `docker-build` job.
**Alternatives rejected**: shared base image `cardswitch-base` (registry dependency, premature); building from the service dir (BuildingBlocks lives outside it — cannot COPY). **Rationale**: per-service is isolated and simple; `backend/` context is mandatory for the shared project; targeted `.csproj` COPY before `restore` gives a cached NuGet layer.

### ADR-3 — Single `.dockerignore` at `backend/` context root (Slice 2)
**Choice**: One `backend/.dockerignore` (the shared context root for all three images) excluding `**/bin`, `**/obj`, `**/*.user`, `.git`, `**/node_modules`, `**/TestResults`, `Dockerfile`, `**/*.md`. No frontend `.dockerignore` this change (see ADR-5 — no frontend image now).
**Alternatives rejected**: per-service `.dockerignore` (build context is `backend/`, not the service dir — a per-service file would be ignored). **Rationale**: `.dockerignore` is resolved relative to the build context; one file at `backend/` covers all three builds and keeps the context small.

### ADR-4 — Frontend prod env: build-time `environment.prod.ts` + `fileReplacements` (Slice 3)
**Choice**: Create `frontend/src/environments/environment.prod.ts` (`production: true`, templatable non-`localhost` URLs, e.g. `apiUrl: 'https://api.example.com/cardvault/api'`, `isoSwitchUrl: '.../isoswitch/api'`). Add to `angular.json` → `configurations.production`:
```json
"fileReplacements": [
  { "replace": "src/environments/environment.ts", "with": "src/environments/environment.prod.ts" }
]
```
`defaultConfiguration` is already `production`, so `ng build` picks it up. Verify the emitted bundle references the prod URLs, not `localhost`.
**Alternatives rejected**: runtime `assets/config.json` + `APP_INITIALIZER` (flexible but requires app-code change — out of scope, deferred); leaving a single `environment.ts` (bundle stays wired to localhost — non-deployable). **Rationale**: build-time substitution is the standard Angular pattern, zero app-code change, sufficient for one Gate-1 environment.

### ADR-5 — DECISION: no frontend Docker image this change; prod build only (open question resolved)
**Choice**: This change produces a **deployable Angular prod bundle** (`ng build --configuration production`) but **NO frontend container image** and no frontend service in compose. A frontend Dockerfile (nginx serving `dist/`) is deferred.
**Alternatives rejected**: nginx image + frontend compose service now (drags in reverse-proxy/CORS-origin/runtime-URL concerns that belong to a deploy slice, not Gate-1 packaging; build-time URLs are baked, so each env needs a rebuild — acceptable for the bundle, awkward as a generic image). **Rationale**: Gate 1 is "prove a green build and shippable artifacts." The bundle is the artifact; containerizing the frontend is a CD concern. Keeps scope and the ~400-line budget tight.

### ADR-6 — DECISION: ship `.env.example`; compose reads `${VARS}`; no secrets committed (open question resolved)
**Choice**: Add `backend/deploy/.env.example` enumerating every required key per service (NOT a committed `.env` with real values). `docker-compose.yml` references `${VAR}` so `docker compose` auto-loads a developer-created `backend/deploy/.env`. `.env` is gitignored; `.env.example` is committed as the contract. Mapping (Ola 0 fail-fast):
| Service | Required env (→ config key) |
|---------|------------------------------|
| CardVault | `Jwt__SigningKey`, `ConnectionStrings__Postgres`, `ConnectionStrings__SqlServerIdentity`, `Kafka__BootstrapServers=kafka:9092`, `Vault__ActiveKeyId`, `Vault__Keys__<id>` |
| IsoSwitch | `Jwt__SigningKey`, `Tokenization__Secret`, `ConnectionStrings__Postgres`, `Kafka__BootstrapServers=kafka:9092` |
| IsoAudit | `Jwt__Key` (NOT `SigningKey` — asymmetry), `ConnectionStrings__IsoSwitchDb`, `Kafka__BootstrapServers=kafka:9092` |
**Alternatives rejected**: committed `.env` (leaks secrets); hardcoding secrets in compose `environment:` (same leak, no override); user-secrets in containers (not available in a built image). **Rationale**: `.env.example` is the documented contract; `${VAR}` indirection keeps real secrets out of git while letting compose boot. The JWT asymmetry is commented inline in compose at each service.

### ADR-7 — Compose remediation + dual-DB provisioning for CardVault (Slice 4)
**Choice**: Fix `bitnamilegacy/kafka:3.7` → `bitnami/kafka:3.7`; fix `KAFKA_CFG_ADVERTISED_LISTENERS` `localhost:9092` → `kafka:9092`. Add three service containers (`build:` context `backend/`, `dockerfile:` the per-service path; `depends_on` postgres/sqlserver/kafka; `env_file: .env` + explicit `kafka:9092`). **CardVault dual-DB**: Postgres `cardvault` DB is created by existing `init-databases.sql`; the SQL Server `CardVaultIdentity` DB is created by EF Identity migrations at CardVault startup (no init script for SQL Server) — compose only needs the `sqlserver` container reachable via `ConnectionStrings__SqlServerIdentity`. IsoAudit reads the `isoswitch` Postgres DB (already provisioned). Document the ~1.5GB SQL Server RAM note in tasks.
**Alternatives rejected**: a SQL Server init script for `CardVaultIdentity` (EF migrations already create it at boot — redundant and risks drift); dropping SQL Server (Identity hard-requires it). **Rationale**: minimal compose edits that make the built images runnable; lean on existing migration-on-boot behavior.

## 3. Data Flow
```
push/PR → main
   ├─ build-test ──restore→build→test (650+ green) ──► docker-build (gated): docker build -f <svc>/Dockerfile backend/ x3
   └─ build-frontend ──npm ci → ng build --configuration production ──► dist/ bundle (prod URLs via fileReplacements)

docker compose up (local):
   .env(.example) ─${VAR}→ compose ─Jwt__/ConnectionStrings__/Kafka__→ service containers
        postgres(cardvault,isoswitch) + sqlserver(CardVaultIdentity via EF migrate) + kafka(advertise kafka:9092)
        └─(missing secret)─► Ola 0 OptionsValidator fail-fast → container exits (intended)
```

## 4. File Changes
| File | Action | Slice |
|------|--------|-------|
| `.github/workflows/ci.yml` | Create | 1 (build-test+build-frontend); 2 adds docker-build job |
| `backend/.dockerignore` | Create | 2 |
| `backend/services/IsoAudit/src/IsoAudit.Api/Dockerfile` | Rewrite | 2 (fix stale path, layer cache, COPY shared, port 5301) |
| `backend/services/CardVault/src/CardVault.Api/Dockerfile` | Create | 2 (port 5101) |
| `backend/services/IsoSwitch/src/IsoSwitch.Api/Dockerfile` | Create | 2 (port 5201) |
| `frontend/src/environments/environment.prod.ts` | Create | 3 (production:true, templatable URLs) |
| `frontend/angular.json` | Modify | 3 (fileReplacements in production config) |
| `backend/deploy/docker-compose.yml` | Modify | 4 (Kafka image+listener; 3 service containers; env_file) |
| `backend/deploy/.env.example` | Create | 4 (per-service required-secret contract) |
| `backend/deploy/.gitignore` (or root) | Modify | 4 (ignore `.env`) |

## 5. Testing Strategy
Strict TDD is active but there is NO production .NET/TS code to red-green — verification is config/file assertion + execution.
| Layer | What to verify | Approach |
|-------|----------------|----------|
| Config (Slice 1) | ci.yml triggers, jobs, pins | Lint YAML; assert pinned `9.0.x`/Node 20, correct sln path, `--no-restore`/`--no-build` flags |
| CI smoke (Slice 1) | build-test + build-frontend green on clean runner | Push to a branch → Actions run green (650+ tests) |
| Execution (Slice 2) | images build with `backend/` context | `docker-build` job green; local `docker build -f <svc>/Dockerfile backend/` succeeds for all 3; no `cardswitch_solution/` references |
| File assertion (Slice 2) | `.dockerignore` excludes bin/obj/node_modules/.git | grep the file; confirm small context |
| Execution (Slice 3) | prod bundle has no `localhost` | `ng build --configuration production`; grep `dist/` for prod URLs, assert no `localhost` |
| Execution (Slice 4) | compose boots all 3 services | `docker compose up`; assert Kafka pulls (`bitnami/kafka:3.7`), advertises `kafka:9092`, services pass Ola 0 fail-fast with `.env` populated |

## 6. Migration / Rollout
No data migration. All deliverables additive infrastructure; rollback = revert the offending per-slice commit (prior state was "no image"/"broken image"/"localhost bundle" — reverting cannot regress a working deployment). `.env` must be populated locally before `docker compose up` (intended fail-fast otherwise).

## 7. Slice Plan (auto-chain, stacked-to-main, ~400-line budget each)
1. **CI core** — `ci.yml` with `build-test` + `build-frontend`. No Docker dependency; lands green first, smallest, highest signal.
2. **Dockerfiles + .dockerignore + docker-build job** — rewrite IsoAudit, new CardVault/IsoSwitch, `backend/.dockerignore`, add gated `docker-build` to `ci.yml`. Verified by the CI job building from `backend/`.
3. **Frontend prod build** — `environment.prod.ts` + `angular.json` fileReplacements. Independent, ~20 lines.
4. **Compose remediation + service containers** — Kafka fixes, three service definitions, `.env.example`, gitignore `.env`. Verify-by-execution (`docker compose up`).
Each slice is independently mergeable and verifiable; ordering 1→2 is a hard dependency (docker-build needs the workflow), 3 and 4 are independent of 2.

## 8. Open Questions
- [x] Frontend image now or prod build only? → prod build only; no frontend container this change. (ADR-5)
- [x] `.env.example` decision? → ship `.env.example` contract, compose reads `${VAR}`, `.env` gitignored, no secrets committed. (ADR-6)
- [x] CardVault dual-DB provisioning? → Postgres `cardvault` via init script; SQL Server `CardVaultIdentity` via EF migrate-on-boot; compose just provides reachable containers. (ADR-7)
- [x] CI test infra? → InMemory, no service-containers. (ADR-1)
- None blocking.
