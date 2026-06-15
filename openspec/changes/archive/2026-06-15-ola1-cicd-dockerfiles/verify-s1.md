# Verify Report — Slice 1: CI Core (build-test + build-frontend)
## Change: ola1-cicd-dockerfiles
## Slice: S1 (CICD-1, CICD-2, CICD-3)
## Branch: feat/ola1-s1-ci-core
## Reviewed commits: 752972b..9db4538
## Date: 2026-06-12
## Verdict: PASS

---

## Test Suite Result

| Project | Tests | Result |
|---------|-------|--------|
| IsoAudit.Tests | 18 | PASS |
| IsoSwitch.Tests | 53 | PASS |
| CardVault.Tests | 579 | PASS |
| **Total** | **650** | **GREEN** |

Build: `dotnet build backend/CardSwitchPlatform.sln -c Release` — 0 errors, 15 warnings (pre-existing nullability warnings, not introduced by S1).
Test run: `dotnet test backend/CardSwitchPlatform.sln -c Release --no-build` — 650 passed, 0 failed, 0 skipped.

The 16 pre-existing CardVault.Tests failures reported in apply-progress (Release-only binary missing when `--no-build` is used without a prior Release build) do NOT reproduce when the correct sequence (`build` then `test --no-build`) is followed. This matches the CI workflow order and confirms CICD-INV-4 holds.

---

## Git Hygiene

| Check | Result |
|-------|--------|
| Working tree clean | PASS — nothing staged, nothing modified |
| Branch up to date with origin | PASS |
| Files changed vs main | 5 files: `.github/workflows/ci.yml` (new, 62 lines) + 4 openspec docs (new) |
| No stray changes outside scope | PASS — only `ci.yml` and openspec planning artifacts |
| Commits | 3: planning docs (752972b), ci.yml (1ecc929), tasks check-off (9db4538) |

`git diff main...feat/ola1-s1-ci-core --stat` output:
- `.github/workflows/ci.yml` +62 lines (new file)
- `openspec/changes/ola1-cicd-dockerfiles/design.md` +124 lines (new)
- `openspec/changes/ola1-cicd-dockerfiles/proposal.md` +129 lines (new)
- `openspec/changes/ola1-cicd-dockerfiles/specs/cicd-packaging/spec.md` +400 lines (new)
- `openspec/changes/ola1-cicd-dockerfiles/tasks.md` +423 lines (new)

No application source files, no Dockerfiles, no compose files touched. Scope discipline: CLEAN.

---

## Spec Compliance Matrix

### CICD-1: GitHub Actions CI Workflow Trigger and Structure

| Requirement | Status | Evidence |
|-------------|--------|----------|
| `.github/workflows/ci.yml` exists | PASS | File present at repo root |
| Triggered on push to main | PASS | `on: push: branches: [main]` |
| Triggered on pull_request to main | PASS | `on: pull_request: branches: [main]` |
| Job `build-test` defined | PASS | Present in ci.yml |
| Job `build-frontend` defined | PASS | Present in ci.yml |
| Job `docker-build` defined | DEFERRED (by design) | S2 adds this job; S1 intentionally omits it. Tasks document: "Do NOT add a docker-build job yet (that is S2)." |
| `build-frontend` has no `needs` (parallel) | PASS | No `needs:` key on build-frontend job |

**Note**: The three-job structure is a full-change contract (CICD-1). S1 delivers two of three jobs as specified by the slice plan. The `docker-build` job absence is expected and tracked in tasks.md T2.2. This is NOT a defect.

### CICD-2: .NET Restore, Build, and Test in CI

| Step | Status | Evidence |
|------|--------|----------|
| `actions/checkout@v4` | PASS | Step present |
| `actions/setup-dotnet@v4` with `dotnet-version: 9.0.x` | PASS | `dotnet-version: 9.0.x` |
| NuGet cache enabled | PASS | `cache: true`, `cache-dependency-path: "**/*.csproj"` |
| `dotnet restore backend/CardSwitchPlatform.sln` | PASS | Exact command matches spec |
| `dotnet build -c Release --no-restore backend/CardSwitchPlatform.sln` | PASS | Exact command with `--no-restore` |
| `dotnet test -c Release --no-build` with `--logger "trx;..."` | PASS | `--logger "trx;LogFileName=results.trx"` present |
| `actions/upload-artifact@v4` uploading `**/TestResults/*.trx` | PASS | `path: "**/TestResults/*.trx"`, `if: always()` |
| Job fails on test failure | PASS | No `continue-on-error`; GitHub default behavior |
| No service containers required | PASS | No `services:` block; tests use EF InMemory + NSubstitute |

### CICD-3: Angular Frontend Production Build in CI

| Step | Status | Evidence |
|------|--------|----------|
| `actions/checkout@v4` | PASS | Step present |
| `actions/setup-node@v4` with `node-version: 20` | PASS | `node-version: 20` |
| npm cache enabled | PASS | `cache: npm`, `cache-dependency-path: frontend/package-lock.json` |
| `npm ci --prefix frontend` | PASS | Exact command matches spec |
| `npm run build --prefix frontend` (equivalent to `ng build --configuration production`) | PASS | `frontend/package.json` build script is `ng build`; `angular.json` has `defaultConfiguration: production` — confirmed in apply-progress T1.3 verification |
| No `needs:` dependency (parallel with build-test) | PASS | No `needs:` key |
| No backend services required | PASS | No `services:` block |

**Known accepted limitation**: `build-frontend` will fail in CI until S3 lands because `environment.prod.ts` and the `angular.json` `fileReplacements` entry do not yet exist. This is explicitly documented in tasks.md T1.3 note and the design's verify-by-execution table. It is NOT a defect in S1.

### CICD-INV-4: 650+ Tests Remain Green

| Status | Evidence |
|--------|----------|
| PASS | 650 tests passed (18 IsoAudit + 53 IsoSwitch + 579 CardVault), 0 failures |

---

## Task Completion

| Task | Description | Status |
|------|-------------|--------|
| T1.1 | Create `.github/workflows/ci.yml` with build-test and build-frontend | COMPLETE |
| T1.2 | Verify build-test locally (config-lint: sln path, dotnet-version, flags) | COMPLETE |
| T1.3 | Verify build-frontend locally (package-lock.json, build script, angular.json production config) | COMPLETE |
| T1.4 | Run 650 tests green + scope check + commit | COMPLETE |

All Slice 1 tasks are checked in tasks.md and confirmed implemented in code.

---

## Design Coherence

| ADR | Decision | Status |
|-----|----------|--------|
| ADR-1 | Single ci.yml, parallel quality jobs, gated docker | PASS — workflow topology matches exactly; `docker-build` deferred to S2 per plan |
| ADR-1 caching | NuGet cache on `**/*.csproj`; npm cache on `frontend/package-lock.json` | PASS — both caches configured |
| ADR-1 gate | `docker-build` needs `build-test` | DEFERRED to S2 — by design |

---

## Findings

No CRITICAL issues. No WARNING issues. One SUGGESTION.

### SUGGESTION S-1: `dotnet test` trx path scoping

The trx logger uses a fixed `LogFileName=results.trx`. When three test assemblies run in parallel (which `dotnet test` on a solution does), each project writes to its own `bin/Release/net9.0/TestResults/results.trx`. The upload glob `**/TestResults/*.trx` captures all three. This works correctly but could produce three files with identical names in the artifact zip, making them harder to distinguish in the GitHub Actions UI. Consider using `--logger "trx;LogFileName={assembly}.trx"` or a per-project path in a future pass. No spec violation — upload-artifact@v4 handles collisions by renaming.

Severity: SUGGESTION — cosmetic, no spec impact.

---

## Known Accepted Limitations

| Item | Description | Disposition |
|------|-------------|-------------|
| `build-frontend` fails in CI until S3 | `environment.prod.ts` and `fileReplacements` are missing; `ng build --configuration production` will exit non-zero on a real runner | ACCEPTED — documented in tasks.md T1.3, design §5, apply-progress Gotchas. S3 resolves it. |
| `docker-build` job absent | Three-job structure (CICD-1 full contract) incomplete until S2 | ACCEPTED — planned, tracked |

---

## Verdict

PASS — 0 CRITICAL, 0 WARNING, 1 SUGGESTION.

All CICD-1, CICD-2, and CICD-3 requirements attributable to Slice 1 are fully implemented and verified.
CICD-INV-4 holds (650 tests green).
Scope is clean (only ci.yml + openspec docs changed vs main).
The `build-frontend` CI failure until S3 is a documented, expected limitation, not a defect.

Next recommended: sdd-apply (Slice 2 — Dockerfiles + .dockerignore + docker-build job).
