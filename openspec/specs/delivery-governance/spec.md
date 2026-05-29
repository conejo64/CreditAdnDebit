# Capability: Delivery Governance

## Purpose

Define how planning artifacts (sprints, backlog) are created, updated, and validated against the current repository state to avoid stale commitments and unverified status claims.

---

## Requirement: Code-Verified Sprint Status

The project SHALL treat sprint status documents as evidence-based records derived from the current repository state rather than historical intent.

### Scenario: Sprint status is rebaselined from code

- WHEN maintainers update `funcional/Sprints.md`
- THEN each partial or completed claim is verified against the current code, tests, and active integrations
- AND stale findings that are already closed in the repository are removed or rewritten as historical notes

---

## Requirement: Hardening Gates Before New Backlog Features

The project MUST revalidate hardening blockers before moving new backlog capabilities into active development.

### Scenario: New backlog work is considered for activation

- WHEN a backlog item such as `v76+` is proposed for implementation
- THEN maintainers first verify the real status of the blocking hardening phases in `funcional/BackLog.md`
- AND new feature work does not start while those blocking phases still have unresolved deliverables verified in code

---

## Requirement: Shared Planning Source Of Truth

The project SHALL align planning artifacts with verified repository evidence before reopening roadmap commitments.

### Scenario: Planning artifacts disagree with the repository

- WHEN `funcional/Sprints.md` or `funcional/BackLog.md` diverge from the current implementation state
- THEN the planning artifacts are updated before new roadmap commitments are made
- AND the next recommended work is derived from the remaining verified gaps instead of stale audit language
