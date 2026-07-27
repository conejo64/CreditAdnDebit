# Delta Spec — Ola 1: CI Pipeline, Dockerfiles & Deployable Builds
# Capability: cicd-packaging
# Change: ola1-cicd-dockerfiles
# Base spec: (no prior base spec — this change introduces new SHALL constraints for build/packaging infrastructure)

This document records ONLY what changes. It describes the WHAT (behavioral contracts), not the HOW (implementation).
Unchanged product behaviors are not repeated here.

---

## Scope Note

All deliverables in this change are build/packaging infrastructure. There are no application-code
behavioral changes. Requirements are expressed as build, pipeline, and packaging contracts.
The success criterion is: a clean GitHub-hosted runner can reproduce a green build, a green test suite,
working container images for all three backend services, and a deployable Angular production bundle.

---

## ADDED Requirements

---

### Requirement CICD-1: GitHub Actions CI Workflow Trigger and Structure

A GitHub Actions workflow file SHALL exist at `.github/workflows/ci.yml`.

The workflow SHALL be triggered on every push and pull-request to the `main` branch.

The workflow SHALL define exactly three jobs:
- `build-test` — restores, builds, and tests the full .NET solution.
- `build-frontend` — installs Node dependencies and produces the Angular production bundle.
- `docker-build` — builds container images for the three backend services.

`docker-build` SHALL declare `needs: build-test` so that it only runs when `build-test` succeeds.
`build-frontend` MAY run in parallel with `build-test` (no `needs` dependency on it).

#### Scenario: Push to main triggers the full workflow

- GIVEN a commit is pushed to the `main` branch
- WHEN GitHub Actions evaluates workflow triggers
- THEN `ci.yml` is enqueued and all three jobs are scheduled
- AND `docker-build` does not start until `build-test` has completed successfully

#### Scenario: Failing build-test blocks docker-build

- GIVEN `build-test` exits with a non-zero exit code
- WHEN GitHub Actions evaluates job dependencies
- THEN `docker-build` is skipped or cancelled
- AND the workflow run is marked as failed

---

### Requirement CICD-2: .NET Restore, Build, and Test in CI

The `build-test` job SHALL:
1. Check out the repository.
2. Set up the .NET SDK pinned to version `9.0.x` via `actions/setup-dotnet@v4`.
3. Execute `dotnet restore` against `backend/CardSwitchPlatform.sln`.
4. Execute `dotnet build -c Release --no-restore` against `backend/CardSwitchPlatform.sln`.
5. Execute `dotnet test -c Release --no-build` against `backend/CardSwitchPlatform.sln`.

The test step SHALL NOT require any service containers (Postgres, SQL Server, Kafka). Tests rely
exclusively on EF InMemory providers and NSubstitute mocks with hosted-service suppression.

The test step SHALL upload `.trx` result files as a workflow artifact (e.g., using `actions/upload-artifact`).

The job SHALL fail (non-zero exit) if any test fails or if the build produces errors.

#### Scenario: All tests pass on a clean runner

- GIVEN a clean GitHub-hosted runner with no pre-installed .NET SDK matching `9.0.x`
- WHEN `build-test` runs restore → build → test
- THEN all tests in `backend/CardSwitchPlatform.sln` pass (650+ tests green)
- AND `.trx` result files are uploaded as an artifact
- AND the job exits with code 0

#### Scenario: A test failure fails the CI job

- GIVEN at least one test in the solution returns a non-zero exit code
- WHEN `dotnet test` completes
- THEN the `build-test` job exits with a non-zero code
- AND the workflow run is marked as failed
- AND `docker-build` does not run

---

### Requirement CICD-3: Angular Frontend Production Build in CI

The `build-frontend` job SHALL:
1. Check out the repository.
2. Set up Node.js pinned to **Node 20 LTS** via `actions/setup-node@v4` (Angular 17 supports `^18.13 || ^20.9`; Node 20 is the active LTS).
3. Execute `npm ci --prefix frontend` to install dependencies from the lockfile.
4. Execute `ng build --configuration production` (or equivalent `npm run build` that invokes it) inside the `frontend/` directory.

The job SHALL fail if `ng build --configuration production` exits with a non-zero code.
The job SHALL NOT require any backend service to be running.

#### Scenario: Frontend prod build succeeds on a clean runner

- GIVEN a clean runner with no pre-installed Node
- WHEN `build-frontend` runs `npm ci` then `ng build --configuration production`
- THEN the Angular CLI produces a bundle in `frontend/dist/`
- AND the job exits with code 0

#### Scenario: Missing production environment file fails the build

- GIVEN `frontend/src/environments/environment.prod.ts` does not exist
- WHEN `ng build --configuration production` is invoked with `fileReplacements` configured
- THEN the build fails with an error referencing the missing file
- AND the job exits with a non-zero code

---

### Requirement CICD-4: Docker Smoke Build for All Three Services in CI

The `docker-build` job SHALL build (not push) container images for:
- CardVault.Api
- IsoSwitch.Api
- IsoAudit.Api

Each image SHALL be built using `backend/` as the Docker build context, with the `--file` flag pointing
to the service-specific Dockerfile path (e.g., `--file backend/services/CardVault/src/CardVault.Api/Dockerfile`).

The job SHALL fail if any `docker build` command exits with a non-zero code.
No image SHALL be pushed to any registry during CI.

#### Scenario: All three docker builds succeed

- GIVEN `build-test` has completed successfully
- WHEN the `docker-build` job runs `docker build` for each of the three services
- THEN all three builds complete without error
- AND no image is pushed or tagged with a registry prefix
- AND the job exits with code 0

---

### Requirement CICD-5: IsoAudit Dockerfile — Correct Paths and Multi-Stage Build

The file `backend/services/IsoAudit/src/IsoAudit.Api/Dockerfile` SHALL NOT reference
the stale path `cardswitch_solution/`. All `COPY`, `RUN dotnet restore`, and `RUN dotnet publish`
instructions SHALL use paths relative to the `backend/` build context.

The Dockerfile SHALL follow the multi-stage pattern:
1. **Build stage** (`FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build`): copies `.csproj` files for
   both `IsoAudit.Api` and `shared/BuildingBlocks`, runs `dotnet restore` for layer caching, then
   copies the remaining source and runs `dotnet publish -c Release -o /app/publish`.
2. **Runtime stage** (`FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final`): copies the publish output,
   sets `ENV ASPNETCORE_URLS=http://+:5301`, exposes port `5301`, and sets the `ENTRYPOINT`.

The shared `BuildingBlocks` project at `shared/BuildingBlocks/BuildingBlocks.csproj` (relative to
`backend/`) SHALL be COPY-ed into the build stage before `dotnet restore` so the dependency resolves.

#### Scenario: IsoAudit builds successfully from backend/ context

- GIVEN the `backend/` directory is the Docker build context
- WHEN `docker build -f backend/services/IsoAudit/src/IsoAudit.Api/Dockerfile backend/` is executed
- THEN the build completes without path resolution errors
- AND the resulting image listens on port `5301`
- AND the image does NOT contain SDK tooling (runtime-only final stage)

#### Scenario: Stale path reference causes build failure (negative — must not occur)

- GIVEN the Dockerfile contains `cardswitch_solution/` in any instruction
- WHEN `docker build` is executed
- THEN the build fails because the path does not exist in the `backend/` context
- AND this scenario MUST NOT occur after this change is applied

---

### Requirement CICD-6: CardVault Dockerfile — New, Multi-Stage, Port 5101

The file `backend/services/CardVault/src/CardVault.Api/Dockerfile` SHALL be created.

It SHALL follow the same multi-stage pattern as CICD-5:
1. **Build stage** using `mcr.microsoft.com/dotnet/sdk:9.0`: copy `CardVault.Api.csproj` and
   `shared/BuildingBlocks/BuildingBlocks.csproj`, restore, copy full source, publish to `/app/publish`.
2. **Runtime stage** using `mcr.microsoft.com/dotnet/aspnet:9.0`: copy publish output,
   set `ENV ASPNETCORE_URLS=http://+:5101`, expose port `5101`, set `ENTRYPOINT`.

The build context SHALL be `backend/`.

#### Scenario: CardVault builds successfully from backend/ context

- GIVEN the `backend/` directory is the Docker build context
- WHEN `docker build -f backend/services/CardVault/src/CardVault.Api/Dockerfile backend/` is executed
- THEN the build completes without errors
- AND the resulting image listens on port `5101`
- AND the image is a runtime-only image (no SDK)

---

### Requirement CICD-7: IsoSwitch Dockerfile — New, Multi-Stage, Port 5201

The file `backend/services/IsoSwitch/src/IsoSwitch.Api/Dockerfile` SHALL be created.

It SHALL follow the same multi-stage pattern as CICD-5:
1. **Build stage** using `mcr.microsoft.com/dotnet/sdk:9.0`: copy `IsoSwitch.Api.csproj` and
   `shared/BuildingBlocks/BuildingBlocks.csproj`, restore, copy full source, publish to `/app/publish`.
2. **Runtime stage** using `mcr.microsoft.com/dotnet/aspnet:9.0`: copy publish output,
   set `ENV ASPNETCORE_URLS=http://+:5201`, expose port `5201`, set `ENTRYPOINT`.

The build context SHALL be `backend/`.

#### Scenario: IsoSwitch builds successfully from backend/ context

- GIVEN the `backend/` directory is the Docker build context
- WHEN `docker build -f backend/services/IsoSwitch/src/IsoSwitch.Api/Dockerfile backend/` is executed
- THEN the build completes without errors
- AND the resulting image listens on port `5201`
- AND the image is a runtime-only image (no SDK)

---

### Requirement CICD-8: .dockerignore Excludes Large / Irrelevant Paths

A `.dockerignore` file SHALL exist at `backend/.dockerignore` (co-located with the `backend/` build context root).

The `.dockerignore` SHALL exclude at minimum:
- `**/bin/`
- `**/obj/`
- `**/.git`
- `**/*.user`
- `**/node_modules/`
- `.vs/`

The intent is to prevent multi-hundred-megabyte directories from being included in the build context
sent to the Docker daemon, making builds faster and the context smaller.

#### Scenario: Build context excludes obj and bin directories

- GIVEN `backend/.dockerignore` is in place with the required exclusions
- WHEN `docker build` is invoked with `backend/` as context
- THEN the build context does NOT contain files under any `bin/` or `obj/` subdirectory
- AND the build context size is materially smaller than without the file

---

### Requirement CICD-9: Angular Production Environment File — No localhost References

The file `frontend/src/environments/environment.prod.ts` SHALL exist with at least the following shape:

```typescript
export const environment = {
  production: true,
  apiUrl: '<non-localhost API URL>',
  isoSwitchUrl: '<non-localhost IsoSwitch URL>'
};
```

The values for `apiUrl` and `isoSwitchUrl` SHALL NOT be `http://localhost:...` URLs.
They SHALL be templatable (e.g., placeholder values that an operator can substitute via
environment-specific configuration or a build-time substitution step).

`frontend/angular.json` SHALL include a `fileReplacements` entry under
`projects.card-switch-portal.architect.build.configurations.production` that maps
`src/environments/environment.ts` → `src/environments/environment.prod.ts`.

#### Scenario: Production bundle does not reference localhost

- GIVEN `environment.prod.ts` exists with non-localhost API URLs
- AND `angular.json` has `fileReplacements` for the production configuration
- WHEN `ng build --configuration production` is executed
- THEN the emitted JavaScript bundle does NOT contain `localhost:5101` or `localhost:5201`
- AND the bundle DOES contain the URL values from `environment.prod.ts`

#### Scenario: Development build is unaffected

- GIVEN `environment.ts` still contains `http://localhost:5101/api`
- WHEN `ng build --configuration development` (or `ng serve`) is executed
- THEN the dev bundle uses `environment.ts` (localhost URLs), not `environment.prod.ts`
- AND existing developer workflow is unbroken

#### Scenario: fileReplacements misconfigured fails prod build (negative guard)

- GIVEN `fileReplacements` references a file path that does not exist
- WHEN `ng build --configuration production` is executed
- THEN the Angular CLI reports a file-not-found error
- AND this scenario MUST NOT occur after this change is applied

---

### Requirement CICD-10: docker-compose Kafka Image Corrected

`backend/deploy/docker-compose.yml` SHALL use the image `bitnami/kafka:3.7` for the `kafka` service.

The value `bitnamilegacy/kafka:3.7` SHALL NOT appear in any compose file in the repository after
this change is applied.

#### Scenario: Kafka service starts with correct image

- GIVEN `docker-compose.yml` specifies `bitnami/kafka:3.7`
- WHEN `docker compose up kafka` is executed on a machine with internet access
- THEN Docker pulls `bitnami/kafka:3.7` without a 404/image-not-found error
- AND the Kafka broker starts successfully

---

### Requirement CICD-11: docker-compose Kafka Advertised Listener Uses Container Hostname

`backend/deploy/docker-compose.yml` SHALL set the Kafka advertised listener to
`PLAINTEXT://kafka:9092` (using the Docker Compose service hostname `kafka`, not `localhost`).

The value `PLAINTEXT://localhost:9092` SHALL NOT appear in the compose file after this change.

#### Scenario: Inter-container Kafka traffic resolves correctly

- GIVEN `KAFKA_CFG_ADVERTISED_LISTENERS=PLAINTEXT://kafka:9092` is configured
- WHEN a backend service container (CardVault, IsoSwitch, IsoAudit) attempts to connect to Kafka
- THEN the connection target `kafka:9092` resolves via Docker's internal DNS
- AND the Kafka client connects without a "bootstrap servers unreachable" error

#### Scenario: External host access still works via published port (informational)

- GIVEN port `9092` is published as `"9092:9092"` in the compose file
- WHEN a process on the host machine connects to `localhost:9092`
- THEN the connection reaches the Kafka broker
- NOTE: this scenario is for local developer tooling; CI tests do not rely on it

---

### Requirement CICD-12: Service Containers in docker-compose with Required Env Vars

`backend/deploy/docker-compose.yml` SHALL include service definitions for CardVault.Api,
IsoSwitch.Api, and IsoAudit.Api.

Each service definition SHALL inject the environment variables required for the service to pass
its Ola 0 fail-fast startup validation. Absent or wrong values cause the container to exit immediately.

**CardVault.Api** SHALL receive:
- `ConnectionStrings__Postgres` — Postgres connection string targeting the `postgres` service
- `ConnectionStrings__SqlServerIdentity` — SQL Server connection string targeting the `sqlserver` service
- `Jwt__SigningKey` — JWT signing key (CardVault reads `Jwt:SigningKey`)
- `Kafka__BootstrapServers=kafka:9092`

**IsoSwitch.Api** SHALL receive:
- `Tokenization__Secret` — tokenization secret (≥32 chars, non-placeholder per SEC-1)
- `Jwt__SigningKey` — JWT signing key (IsoSwitch reads `Jwt:SigningKey`)
- `Kafka__BootstrapServers=kafka:9092`

**IsoAudit.Api** SHALL receive:
- `Jwt__Key` — JWT key (IsoAudit reads `Jwt:Key`, NOT `Jwt:SigningKey`)
- `ConnectionStrings__IsoSwitchDb` — Postgres connection string targeting the `postgres` service (IsoAudit reads `GetConnectionString("IsoSwitchDb")`, NOT `"Postgres"`)
- `Kafka__BootstrapServers=kafka:9092`

The JWT key asymmetry (IsoAudit uses `Jwt__Key`; CardVault and IsoSwitch use `Jwt__SigningKey`) SHALL
be documented via inline comments in the compose file.

A `.env.example` file SHALL exist alongside `docker-compose.yml` documenting which variables operators
must supply (JWT secrets, tokenization secret, connection strings).

#### Scenario: CardVault container starts with all required env vars

- GIVEN all required env vars for CardVault.Api are injected via compose
- WHEN `docker compose up cardvault` is executed
- THEN the CardVault container starts, passes fail-fast validation, and reaches a healthy state
- AND no "Configuration validation failed" or similar startup error is logged

#### Scenario: IsoAudit container receives Jwt__Key (not Jwt__SigningKey)

- GIVEN `docker-compose.yml` injects `Jwt__Key` for IsoAudit (not `Jwt__SigningKey`)
- WHEN the IsoAudit container starts
- THEN JWT authentication is operative (IsoAudit resolves `Jwt:Key` from `Jwt__Key`)
- AND no key-not-found configuration error is logged at startup

#### Scenario: Missing secret causes container to fail fast (negative — must not silently accept)

- GIVEN `Tokenization__Secret` is absent from IsoSwitch's env block
- WHEN `docker compose up isoswitch` is executed
- THEN the IsoSwitch container exits with a non-zero code (fail-fast per SEC-1)
- AND `docker compose up` reports the service as unhealthy/exited

---

### Requirement CICD-13: Secret-Scanning CI Job That Fails on Detection

`.github/workflows/ci.yml` SHALL include a secret-scanning job (using gitleaks or TruffleHog) that runs on
every push and pull-request to `main` (consistent with the base CICD-1 trigger). The job SHALL scan the
repository — including the commit range under review — and SHALL exit with a non-zero code, failing the
workflow run, when a secret is detected. When no secret is detected, the job SHALL exit with code 0.

The job MAY use a tuned allowlist / baseline so that the committed `.env.example` obvious placeholders and
other known non-secret values do not produce false positives, but the allowlist SHALL NOT suppress detection
of the previously leaked classes of secret (vault keys, DB passwords, admin credentials, admin API keys).

#### Scenario: Pushing a commit containing a secret fails the pipeline

- GIVEN a commit introduces a value matching a secret pattern (e.g. a Base64 AES-256 key or an inline DB password)
- WHEN the secret-scanning job runs in CI
- THEN the job exits with a non-zero code
- AND the workflow run is marked as failed

#### Scenario: A clean commit passes the secret-scanning job

- GIVEN a commit introduces no secret material
- AND any placeholder values are covered by the tuned allowlist / baseline
- WHEN the secret-scanning job runs
- THEN the job exits with code 0
- AND the workflow run is not failed by this job

#### Scenario: Placeholder values in .env.example do not trigger a false positive

- GIVEN `.env.example` contains obvious non-secret placeholder values
- WHEN the secret-scanning job runs
- THEN the placeholders are not reported as secrets
- AND the job does not fail solely because of the `.env.example` placeholders

---

### Requirement CICD-14: Pre-Commit Secret-Scanning Hook

The repository SHALL provide a committed pre-commit hook configuration (e.g. `.pre-commit-config.yaml` or an
equivalent committed git-hook definition) that runs the same class of secret scanner locally and blocks a
commit when a secret is detected in the staged changes, so a secret is caught before it enters history. The
hook SHALL be reproducible from committed configuration (an operator can install it without ad-hoc setup).

#### Scenario: Staged secret blocks the commit locally

- GIVEN the pre-commit hook is installed from the committed configuration
- AND a staged change contains a value matching a secret pattern
- WHEN the developer attempts to commit
- THEN the pre-commit hook exits non-zero
- AND the commit is aborted before the secret enters history

#### Scenario: Clean staged changes commit normally

- GIVEN the pre-commit hook is installed
- AND the staged changes contain no secret material
- WHEN the developer commits
- THEN the hook passes and the commit proceeds

#### Scenario: Pre-commit configuration is committed and reproducible

- GIVEN a fresh clone of the repository
- WHEN an operator installs the pre-commit hooks from the committed configuration
- THEN the secret-scanning hook is registered without additional ad-hoc setup

---

## Invariants (SHALL NOT Change)

The following behaviors are unchanged and SHALL remain true after this change is applied:

- **CICD-INV-1**: No application source code is modified. `.csproj` files, C# source, and Angular
  TypeScript/HTML/SCSS files (outside `environments/`) are untouched.
- **CICD-INV-2**: The development build (`ng build` / `ng serve` without `--configuration production`)
  continues to work and uses `environment.ts` (localhost URLs).
- **CICD-INV-3**: Existing `docker-compose.observability.yml` (Jaeger/Prometheus stack) is untouched.
- **CICD-INV-4**: All 650+ existing tests continue to pass on the `main` branch after this change.
- **CICD-INV-5**: No container image is pushed to any registry by CI. Build-only.
- **CICD-INV-6**: The `Jwt:Key` / `Jwt:SigningKey` asymmetry in application code is NOT reconciled.
  Compose maps env vars correctly per service; the code difference is preserved unchanged.

---

## Out-of-Scope Confirmations

The following are explicitly NOT requirements of this change:

- CD pipeline, registry push (GHCR/ECR), or cloud/VM deployment.
- HSM / Vault server integration; secrets remain injected via compose env / user-secrets.
- Runtime frontend configuration (`assets/config.json` + `APP_INITIALIZER`).
- Testcontainers or real-database integration tests in CI.
- Multi-environment matrix builds or parallel Docker matrix.
- Reconciling `Jwt:Key` vs `Jwt:SigningKey` in application code.
