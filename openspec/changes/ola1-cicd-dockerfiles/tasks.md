# Tasks: Ola 1 — CI Pipeline, Dockerfiles & Deployable Builds
## Change: `ola1-cicd-dockerfiles`
## Generated: 2026-06-12
## Artifact store: hybrid (Engram + openspec)
## Delivery strategy: auto-chain · Chain strategy: stacked-to-main

---

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | S1: ~80 · S2: ~270 · S3: ~30 · S4: ~150 · Total: ~530 |
| 400-line budget risk per slice | S1: Low · S2: Medium · S3: Low · S4: Low |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (S1) → PR 2 (S2) → PR 3 (S3) → PR 4 (S4) |
| Delivery strategy | auto-chain |
| Chain strategy | stacked-to-main |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: Medium (S2 peak; all others Low)

### Suggested Work Units

| Unit | Goal | Likely PR | Spec refs | Est. lines | Notes |
|------|------|-----------|-----------|------------|-------|
| S1 | CI core: `build-test` + `build-frontend` | PR 1 | CICD-1, CICD-2, CICD-3 | ~80 | Base = main; green signal first; no Docker dependency |
| S2 | Dockerfiles + `.dockerignore` + `docker-build` job | PR 2 | CICD-4, CICD-5, CICD-6, CICD-7, CICD-8 | ~270 | Base = PR 1 merged; largest slice; 3 Dockerfiles + dockerignore + ci.yml delta |
| S3 | Frontend prod env | PR 3 | CICD-9 | ~30 | Base = PR 2 merged; fully independent; ~20 lines net |
| S4 | Compose remediation + service containers | PR 4 | CICD-10, CICD-11, CICD-12 | ~150 | Base = PR 3 merged; verify-by-execution only |

---

## Requirement → Task Traceability Matrix

| Requirement | Tasks |
|-------------|-------|
| CICD-1 | 1.1, 1.2 |
| CICD-2 | 1.1, 1.2 |
| CICD-3 | 1.1, 1.3 |
| CICD-4 | 2.2 |
| CICD-5 | 2.1 |
| CICD-6 | 2.1 |
| CICD-7 | 2.1 |
| CICD-8 | 2.3 |
| CICD-9 | 3.1, 3.2 |
| CICD-10 | 4.1 |
| CICD-11 | 4.1 |
| CICD-12 | 4.2, 4.3 |

---

## Dependency Graph

```
S1 (CI core) ──► S2 (Dockerfiles + docker-build job)
                          │
                          ▼ (both mergeable once S2 lands; ordered for clean stack)
                 S3 (frontend prod env)
                          │
                          ▼
                 S4 (compose remediation)
```

S1 → S2 is the only hard dependency (the `docker-build` job added in S2 references the workflow structure from S1).
S3 and S4 are logically independent of each other but are sequenced for a clean stacked-to-main linear history.

---

## Cross-Slice Constraints

- No production .NET or Angular application code changes in any slice — all deliverables are infra/config.
- CICD-INV-4: `dotnet test backend/CardSwitchPlatform.sln` must remain 650+ green throughout.
- CICD-INV-1/2/3: `.csproj`, C# source, `docker-compose.observability.yml` are untouched.
- CICD-INV-5: No `docker push` in any workflow step.
- Strict TDD is active but there is NO red/green cycle here — verification is config-lint + verify-by-execution (CI smoke, `docker build`, `ng build` grep, `docker compose up`).
- `.dockerignore` MUST reside at `backend/` (the build context root). A per-service `.dockerignore` is ignored by Docker.
- IsoAudit uses `Jwt__Key`; CardVault and IsoSwitch use `Jwt__SigningKey`. This asymmetry is preserved unchanged (CICD-INV-6). Compose must map env vars accordingly.

---

## Slice 1 — CI Core (build-test + build-frontend)

**Branch**: `feat/ola1-s1-ci-core`
**Base**: `main`
**Goal**: A green `.github/workflows/ci.yml` that runs `build-test` (restore → build → test → upload .trx) and `build-frontend` (npm ci → ng build --configuration production) in parallel on every push/PR to `main`. No Docker step yet.
**Spec refs**: CICD-1, CICD-2, CICD-3
**Estimated lines**: ~80 (one new file)
**Rollback boundary**: Revert the single `ci.yml` file. No other files touched.

### Task 1.1 — Create `.github/workflows/ci.yml` with `build-test` and `build-frontend` jobs

- [x] Create `.github/workflows/ci.yml`.
- [x] Set triggers: `on: push` and `pull_request` targeting `main` only.
- [x] Define `build-test` job (`runs-on: ubuntu-latest`):
  - Step: `actions/checkout@v4`
  - Step: `actions/setup-dotnet@v4` with `dotnet-version: 9.0.x`; enable NuGet cache (`cache: true`) keyed on `**/*.csproj`
  - Step: `dotnet restore backend/CardSwitchPlatform.sln`
  - Step: `dotnet build -c Release --no-restore backend/CardSwitchPlatform.sln`
  - Step: `dotnet test -c Release --no-build --logger "trx;LogFileName=results.trx" backend/CardSwitchPlatform.sln`
  - Step: `actions/upload-artifact@v4` uploading `**/TestResults/*.trx` — always runs (`if: always()`)
- [x] Define `build-frontend` job (`runs-on: ubuntu-latest`, NO `needs` — runs parallel):
  - Step: `actions/checkout@v4`
  - Step: `actions/setup-node@v4` with `node-version: 20`; npm cache enabled (`cache: npm`, `cache-dependency-path: frontend/package-lock.json`)
  - Step: `npm ci --prefix frontend`
  - Step: `npm run build --prefix frontend` (invokes `ng build --configuration production` via `package.json` script)
- [x] Do NOT add a `docker-build` job yet (that is S2).
- **Spec ref**: CICD-1 (trigger + structure), CICD-2 (build-test detail), CICD-3 (build-frontend detail)

### Task 1.2 — Verify `build-test` locally (config-lint)

- [x] Confirm `backend/CardSwitchPlatform.sln` path is correct (`backend/CardSwitchPlatform.sln` exists at repo root → `backend/`).
- [x] Confirm `dotnet-version: 9.0.x` matches the project's `<TargetFramework>net9.0</TargetFramework>`.
- [x] Lint `ci.yml` for YAML syntax (e.g., `yamllint` or GitHub Actions VS Code extension).
- [x] Confirm `--no-restore` flag is present on `dotnet build` and `--no-build` on `dotnet test`.
- **Spec ref**: CICD-2

### Task 1.3 — Verify `build-frontend` locally (config-lint)

- [x] Confirm `frontend/package-lock.json` exists (required for `npm ci`).
- [x] Confirm `package.json` has a `"build"` script that calls `ng build --configuration production` (or equivalent).
- [x] Confirm `angular.json` has a `production` configuration (it does; `defaultConfiguration` is already `production`).
- **Spec ref**: CICD-3
- **Note**: `environment.prod.ts` and `fileReplacements` do not exist yet; `ng build --configuration production` will fail until S3 lands. The `build-frontend` job in CI will be truly green only after S3 merges. This is expected — S1 proves the workflow plumbing; S3 makes the frontend build pass.

### Task 1.4 — Verify slice integrity

- [x] Run `dotnet test backend/CardSwitchPlatform.sln` locally — confirm 650 green (CICD-INV-4). ✓ 650 passed (18 IsoAudit + 53 IsoSwitch + 579 CardVault).
- [x] Confirm `.github/workflows/ci.yml` is the only file changed in this slice (plus openspec planning artifacts committed separately).
- [x] Commit: `feat(ci): add build-test and build-frontend jobs to ci.yml`
- **Spec ref**: CICD-INV-4

---

## Slice 2 — Dockerfiles + .dockerignore + docker-build job

**Branch**: `feat/ola1-s2-dockerfiles`
**Base**: `feat/ola1-s1-ci-core` (or `main` after S1 merges)
**Goal**: Three multi-stage Dockerfiles buildable from the `backend/` context, a `backend/.dockerignore`, and the gated `docker-build` job added to `ci.yml`. IsoAudit Dockerfile is a rewrite; CardVault and IsoSwitch are new.
**Spec refs**: CICD-4, CICD-5, CICD-6, CICD-7, CICD-8
**Estimated lines**: ~270 (3 Dockerfiles ≈ 35 lines each; .dockerignore ≈ 10 lines; ci.yml delta ≈ 20 lines)
**Rollback boundary**: Revert the 5 changed files (3 Dockerfiles, `.dockerignore`, `ci.yml`). Losing the images doesn't regress a running deployment.

### Task 2.1 — Write the three multi-stage Dockerfiles

**Shared skeleton** (apply to all three; only `<Svc>`, `<port>`, and `.csproj` paths vary):

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY shared/BuildingBlocks/BuildingBlocks.csproj shared/BuildingBlocks/
COPY services/<Svc>/src/<Svc>.Api/<Svc>.Api.csproj services/<Svc>/src/<Svc>.Api/
# Copy each transitive .csproj for NuGet layer cache:
COPY services/<Svc>/src/<Svc>.Application/<Svc>.Application.csproj services/<Svc>/src/<Svc>.Application/
COPY services/<Svc>/src/<Svc>.Domain/<Svc>.Domain.csproj services/<Svc>/src/<Svc>.Domain/
COPY services/<Svc>/src/<Svc>.Infrastructure.*/<Svc>.Infrastructure.*.csproj services/<Svc>/src/<Svc>.Infrastructure.*/
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

- [x] **IsoAudit — REWRITE** `backend/services/IsoAudit/src/IsoAudit.Api/Dockerfile`:
  - Port: `5301`; entry DLL: `IsoAudit.Api.dll`
  - Verify: `grep -r "cardswitch_solution" backend/services/IsoAudit/src/IsoAudit.Api/Dockerfile` returns empty (stale path gone)
  - Copy `shared/BuildingBlocks/BuildingBlocks.csproj` before `dotnet restore` (resolves cross-service dependency)
  - **Spec ref**: CICD-5

- [x] **CardVault — CREATE** `backend/services/CardVault/src/CardVault.Api/Dockerfile`:
  - Port: `5101`; entry DLL: `CardVault.Api.dll`
  - Include all CardVault `.csproj` COPY steps before restore (Application, Domain, Infrastructure.*)
  - **Spec ref**: CICD-6

- [x] **IsoSwitch — CREATE** `backend/services/IsoSwitch/src/IsoSwitch.Api/Dockerfile`:
  - Port: `5201`; entry DLL: `IsoSwitch.Api.dll`
  - Include all IsoSwitch `.csproj` COPY steps before restore
  - **Spec ref**: CICD-7

### Task 2.2 — Add gated `docker-build` job to `ci.yml`

- [x] Add a third job `docker-build` to `.github/workflows/ci.yml` with `needs: build-test`.
- [x] Three steps, one per service:
  ```yaml
  - name: Build CardVault image
    run: docker build -f backend/services/CardVault/src/CardVault.Api/Dockerfile backend/
  - name: Build IsoSwitch image
    run: docker build -f backend/services/IsoSwitch/src/IsoSwitch.Api/Dockerfile backend/
  - name: Build IsoAudit image
    run: docker build -f backend/services/IsoAudit/src/IsoAudit.Api/Dockerfile backend/
  ```
- [x] Confirm: no `docker push` step, no `--tag` with a registry prefix.
- **Spec ref**: CICD-4

### Task 2.3 — Create `backend/.dockerignore`

- [x] Create `backend/.dockerignore` (at the build context root — NOT inside a service folder).
- [x] Include at minimum:
  ```
  **/bin/
  **/obj/
  **/.git
  **/*.user
  **/node_modules/
  .vs/
  **/TestResults/
  **/*.md
  ```
- [x] Confirm the file is at `backend/.dockerignore`, not `backend/services/.../.dockerignore`.
- **Spec ref**: CICD-8 (ADR-3: `.dockerignore` is resolved relative to the build context)

### Task 2.4 — Verify locally: all three images build from `backend/` context

- [x] From repo root, run:
  ```
  docker build -f backend/services/CardVault/src/CardVault.Api/Dockerfile backend/
  docker build -f backend/services/IsoSwitch/src/IsoSwitch.Api/Dockerfile backend/
  docker build -f backend/services/IsoAudit/src/IsoAudit.Api/Dockerfile backend/
  ```
- [x] Each build must exit with code 0.
- [x] Confirm no `cardswitch_solution/` path references remain in any Dockerfile.
- [x] Run `dotnet test backend/CardSwitchPlatform.sln` — 650+ green (CICD-INV-4). ✓ 650 passed (18 IsoAudit + 53 IsoSwitch + 579 CardVault).
- **Spec ref**: CICD-4, CICD-5, CICD-6, CICD-7

### Task 2.5 — Commit and slice integrity

- [x] Files changed: `backend/services/IsoAudit/src/IsoAudit.Api/Dockerfile` (rewrite), `backend/services/CardVault/src/CardVault.Api/Dockerfile` (new), `backend/services/IsoSwitch/src/IsoSwitch.Api/Dockerfile` (new), `backend/.dockerignore` (new), `.github/workflows/ci.yml` (add docker-build job).
- [x] Commit: `feat(docker): add multi-stage Dockerfiles, .dockerignore, and docker-build CI job`
- **Spec ref**: CICD-4 through CICD-8

---

## Slice 3 — Frontend Production Environment

**Branch**: `feat/ola1-s3-frontend-prod-env`
**Base**: `feat/ola1-s2-dockerfiles` (or `main` after S2 merges — fully independent)
**Goal**: `environment.prod.ts` with non-localhost URLs + `fileReplacements` in `angular.json`. The `ng build --configuration production` must succeed and the bundle must not contain localhost references.
**Spec refs**: CICD-9
**Estimated lines**: ~30 (`environment.prod.ts` ≈ 7 lines; `angular.json` delta ≈ 5 lines)
**Rollback boundary**: Revert `environment.prod.ts` and the `angular.json` delta. Dev build is unaffected.

### Task 3.1 — Create `frontend/src/environments/environment.prod.ts`

- [x] Create `frontend/src/environments/environment.prod.ts`:
  ```typescript
  export const environment = {
    production: true,
    apiUrl: 'https://api.example.com/cardvault/api',
    isoSwitchUrl: 'https://api.example.com/isoswitch/api'
  };
  ```
- [x] Confirm: neither `apiUrl` nor `isoSwitchUrl` contains `http://localhost`.
- [x] Confirm: `frontend/src/environments/environment.ts` (dev) is NOT modified (CICD-INV-2).
- **Spec ref**: CICD-9

### Task 3.2 — Add `fileReplacements` to `angular.json` production configuration

- [x] Open `frontend/angular.json`.
- [x] Under `projects.card-switch-portal.architect.build.configurations.production`, add:
  ```json
  "fileReplacements": [
    {
      "replace": "src/environments/environment.ts",
      "with": "src/environments/environment.prod.ts"
    }
  ]
  ```
- [x] Confirm `defaultConfiguration` is already `"production"` (do not change it).
- [x] Confirm the `development` configuration is untouched.
- **Spec ref**: CICD-9 (ADR-4)

### Task 3.3 — Verify: prod bundle has no localhost references

- [x] Run `ng build --configuration production` from `frontend/` (or `npm run build --prefix frontend`).
- [x] Confirm build exits with code 0 and produces `frontend/dist/`.
- [x] Grep: confirm `frontend/dist/` does NOT contain `localhost:5101` or `localhost:5201`.
- [ ] Confirm: `ng serve` (dev) still works and uses `environment.ts` localhost URLs (CICD-INV-2).
- [x] Run `dotnet test backend/CardSwitchPlatform.sln` — 650+ green (CICD-INV-4).
- **Spec ref**: CICD-9 all scenarios

### Task 3.4 — Commit and slice integrity

- [x] Files changed: `frontend/src/environments/environment.prod.ts` (new), `frontend/angular.json` (modified).
- [x] Commit: `feat(frontend): add environment.prod.ts and angular.json fileReplacements for production build`
- **Spec ref**: CICD-9

---

## Slice 4 — Compose Remediation + Service Containers

**Branch**: `feat/ola1-s4-compose-services`
**Base**: `feat/ola1-s3-frontend-prod-env` (or `main` after S3 merges — independent)
**Goal**: Fix Kafka image and advertised listener in compose; add three service container definitions with correct env vars; ship `.env.example`; gitignore `.env`.
**Spec refs**: CICD-10, CICD-11, CICD-12
**Estimated lines**: ~150 (`docker-compose.yml` delta ≈ 120 lines; `.env.example` ≈ 25 lines; `.gitignore` delta ≈ 2 lines)
**Rollback boundary**: Revert `docker-compose.yml`, `.env.example`, `.gitignore`. No running prod deployment depends on this.

### Task 4.1 — Fix Kafka image and advertised listener in `docker-compose.yml`

- [ ] In `backend/deploy/docker-compose.yml`, change the `kafka` service image:
  - From: `bitnamilegacy/kafka:3.7`
  - To: `bitnami/kafka:3.7`
- [ ] Change the Kafka advertised listener:
  - From: `KAFKA_CFG_ADVERTISED_LISTENERS=PLAINTEXT://localhost:9092`
  - To: `KAFKA_CFG_ADVERTISED_LISTENERS=PLAINTEXT://kafka:9092`
- [ ] Confirm: `grep -r "bitnamilegacy" backend/deploy/` returns empty.
- [ ] Confirm: `grep -r "localhost:9092" backend/deploy/` returns empty.
- **Spec ref**: CICD-10, CICD-11

### Task 4.2 — Add service container definitions for CardVault, IsoSwitch, IsoAudit

- [ ] Add `cardvault` service to `backend/deploy/docker-compose.yml`:
  ```yaml
  cardvault:
    build:
      context: ../../backend
      dockerfile: services/CardVault/src/CardVault.Api/Dockerfile
    ports:
      - "5101:5101"
    env_file: .env
    environment:
      - Kafka__BootstrapServers=kafka:9092
    depends_on:
      - postgres
      - sqlserver
      - kafka
  ```
  - Required env vars (from `.env` via `env_file`): `ConnectionStrings__Postgres`, `ConnectionStrings__SqlServerIdentity`, `Jwt__SigningKey` (CardVault reads `Jwt:SigningKey`)
  - Note: `ConnectionStrings__SqlServerIdentity` points to `sqlserver` service. EF Identity migrations create `CardVaultIdentity` DB at startup — no init script needed.

- [ ] Add `isoswitch` service:
  ```yaml
  isoswitch:
    build:
      context: ../../backend
      dockerfile: services/IsoSwitch/src/IsoSwitch.Api/Dockerfile
    ports:
      - "5201:5201"
    env_file: .env
    environment:
      - Kafka__BootstrapServers=kafka:9092
    depends_on:
      - postgres
      - kafka
  ```
  - Required env vars (from `.env`): `Tokenization__Secret`, `Jwt__SigningKey`

- [ ] Add `isoaudit` service:
  ```yaml
  isoaudit:
    build:
      context: ../../backend
      dockerfile: services/IsoAudit/src/IsoAudit.Api/Dockerfile
    ports:
      - "5301:5301"
    env_file: .env
    environment:
      - Kafka__BootstrapServers=kafka:9092
    depends_on:
      - postgres
      - kafka
  ```
  - Required env vars (from `.env`): `Jwt__Key` (IsoAudit reads `Jwt:Key` — NOT `Jwt:SigningKey`), `ConnectionStrings__IsoSwitchDb` (IsoAudit reads `GetConnectionString("IsoSwitchDb")`)
  - Add inline YAML comment on `Jwt__Key`: `# IsoAudit uses Jwt__Key (not Jwt__SigningKey) — see CICD-INV-6`

- [ ] Confirm: all three service entries use `kafka:9092` (not `localhost:9092`).
- **Spec ref**: CICD-12 (ADR-6, ADR-7)

### Task 4.3 — Create `backend/deploy/.env.example` and gitignore `.env`

- [ ] Create `backend/deploy/.env.example` documenting all required variables:
  ```
  # ── CardVault ────────────────────────────────────────────────
  # JWT signing key (≥32 chars, non-placeholder) — reads Jwt:SigningKey
  Jwt__SigningKey=

  # Postgres connection string (cardvault DB, created by init-databases.sql)
  ConnectionStrings__Postgres=Host=postgres;Database=cardvault;Username=postgres;Password=...

  # SQL Server connection string (CardVaultIdentity DB, created by EF migrations at startup)
  ConnectionStrings__SqlServerIdentity=Server=sqlserver;Database=CardVaultIdentity;...

  # ── IsoSwitch ────────────────────────────────────────────────
  # Tokenization secret (≥32 chars, non-placeholder) — reads Tokenization:Secret
  Tokenization__Secret=

  # JWT signing key — reads Jwt:SigningKey (same key as CardVault if shared)
  # Jwt__SigningKey already listed above — same value used by IsoSwitch

  # ── IsoAudit ─────────────────────────────────────────────────
  # JWT key — reads Jwt:Key (NOT Jwt:SigningKey — asymmetry from Ola 0, CICD-INV-6)
  Jwt__Key=

  # IsoAudit reads GetConnectionString("IsoSwitchDb") — reads the isoswitch Postgres DB
  ConnectionStrings__IsoSwitchDb=Host=postgres;Database=isoswitch;Username=postgres;Password=...

  # ── CORS (optional for local dev) ────────────────────────────
  Cors__AllowedOrigins__0=http://localhost:4200
  ```
- [ ] Add `backend/deploy/.env` to root `.gitignore` (or `backend/deploy/.gitignore`) so real secrets are never committed.
- [ ] Confirm: `.env.example` is committed; `.env` is not tracked.
- **Spec ref**: CICD-12 (ADR-6)

### Task 4.4 — Verify by execution: compose boots all three services

- [ ] Copy `backend/deploy/.env.example` to `backend/deploy/.env`; fill in test values (strong random secrets for `Jwt__SigningKey`, `Jwt__Key`, `Tokenization__Secret`).
- [ ] Run `docker compose -f backend/deploy/docker-compose.yml up kafka` — confirm `bitnami/kafka:3.7` pulls successfully and Kafka starts.
- [ ] Run `docker compose -f backend/deploy/docker-compose.yml up cardvault isoswitch isoaudit` — confirm each service starts and passes Ola 0 fail-fast validation (no "Configuration validation failed" in logs).
- [ ] Remove test value for `Tokenization__Secret` from `.env`; confirm IsoSwitch container exits non-zero (CICD-12 negative scenario).
- [ ] Run `dotnet test backend/CardSwitchPlatform.sln` — 650+ green (CICD-INV-4).
- **Spec ref**: CICD-10, CICD-11, CICD-12 all scenarios

### Task 4.5 — Commit and slice integrity

- [ ] Files changed: `backend/deploy/docker-compose.yml` (modified), `backend/deploy/.env.example` (new), `.gitignore` or `backend/deploy/.gitignore` (modified).
- [ ] Commit: `feat(compose): fix Kafka image/listener, add service containers and .env.example`
- **Spec ref**: CICD-10, CICD-11, CICD-12
