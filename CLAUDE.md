# ZitronSystem - Claude Code Instructions

Card issuing core and ISO 8583 switch for Ecuador / Latin America (CardVault, IsoSwitch, IsoAudit; Angular front end). Money moves through this code: the rules below are not optional.

## Workflow (mandatory)

- **Every behaviour change goes through SDD** (Spec-Driven Development): explore → propose → spec → design → tasks → apply → verify → archive. Artifacts live under `openspec/changes/<change>/` (OpenSpec) with the Engram mirror the SDD skills maintain. A one-line mechanical fix with a test does not need propose/spec/design; everything that changes what the system does, does.
- **Every code change is strict TDD.** Observe RED on a behaviour test before implementing, then GREEN, then refactor while green. "Compiles" is not RED; a failing assertion is. Never invent RED/GREEN evidence; if a runner is unavailable, say so and run the proportionate structural check instead.
- **Money paths are proven on real PostgreSQL**, not only on the EF InMemory provider: `backend/services/CardVault/tests/CardVault.IntegrationTests` and `backend/services/IsoSwitch/tests/IsoSwitch.IntegrationTests` (Testcontainers, or `CARDVAULT_TEST_POSTGRES` / `ISOSWITCH_TEST_POSTGRES`). InMemory hides `DateTime.Kind`, `HasMaxLength` and unique-index violations.
- **Run tests one project at a time**, never the whole solution on the dev machine (OOM disguised as Roslyn/MSBuild crashes): `dotnet build-server shutdown`, then `dotnet test <project>.csproj -m:1 -p:UseSharedCompilation=false -nodereuse:false`. The runner summary line is Spanish (`Con error: N, Superado: N, Total: N`); trust it, not the exit code.
- **Delivery**: Conventional Commits, one reviewable work unit per commit, tests and docs with the behaviour they prove. No `Co-Authored-By` or AI attribution in commits or PR bodies. Changes above ~400 authored lines ship as a feature-branch chain with a draft tracker PR; the CI trigger for chain branches (`pull_request.branches`) and any `.gitleaks.toml` allowlist entries go in the **first** commit of the chain. Generated output (EF migrations, Designer, snapshots) goes in its own commit before the authored code.
- **Receipt-driven native review** stays on; it is informational and never authorizes delivery. Push, PR creation and merge are human decisions.

## Model routing for delegated work

When delegating through subagents, pick the model by phase. Reasoning-heavy, low-volume phases get the most capable model; bounded implementation gets the best cost/quality model; mechanical phases get the cheapest.

| Phase | Model | Rationale |
|---|---|---|
| SDD explore, propose, spec, design, tasks | **Claude Fable 5.1** (`fable`) | Decisions are made here; few tokens, maximum reasoning. Slicing tasks well is what keeps reviews under budget. |
| SDD apply (writing code and tests) | **Claude Sonnet 5.5** (`sonnet`) by default; **Claude Opus 5.5** (`opus`) when the task touches money paths (ledger, billing, holds, settlement), authentication/authorization, or cryptography/PAN handling, or when a writer reports `partial`/`blocked` | Bounded TDD tasks are where Sonnet earns its cost; on hot paths one missed defect costs more than the model difference. |
| SDD verify, code review, judgment-day judges and refuters | **Claude Opus 5.5** (`opus`) | Adversarial reading against a contract. |
| SDD archive and other mechanical bookkeeping | **Claude Haiku** (`haiku`) | Moving and merging artifacts needs no reasoning. |

Known limits: the native receipt-driven reviewer runs through the `claude` CLI with its own model configuration, and plugin-defined agents (`sdd-*`, `jd-*`) carry a default model — override it with the Agent tool's `model` parameter when launching them. Record deviations from this table in the feature document with the reason.

## Repository conventions

- PR bodies: summary, root cause, changes table, test plan, and a Chain Context section when the PR belongs to a chain. Branch names: `^(feat|fix|chore|docs|style|refactor|perf|test|build|ci|revert)/[a-z0-9._-]+$`.
- Secrets: never commit credentials or key material; `.gitleaks.toml` allowlist entries must be anchored to the exact non-secret line (CI service containers, documented examples), never widened.
- Runbooks live in `docs/runbooks/`; feature task documents in `odd/tasks/` (untracked, mirrored to Engram).
