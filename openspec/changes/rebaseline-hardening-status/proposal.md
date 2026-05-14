# Proposal: Rebaseline Hardening Status

## Why

`funcional/Sprints.md` and `funcional/BackLog.md` currently mix verified implementation status with stale audit notes. Recent review against the repository showed that some previously reported gaps are already closed in code, while other hardening gaps still need explicit verification. Continuing with new backlog features from that stale planning state would risk prioritizing the wrong work.

## Scope

Create a code-first rebaseline for project planning with:

- verification of sprint and backlog claims against the current repository state
- updates to `funcional/Sprints.md` so sprint status reflects evidence instead of intent
- updates to `funcional/BackLog.md` so hardening priorities match the real remaining gaps
- a documented rule that new backlog features do not enter active development until the real hardening blockers are revalidated

## Out Of Scope

- implementation of new business backlog items such as `v76+`
- broad backend refactors beyond what is needed to verify current status
- archival of the change before the rebaseline edits are actually completed

## Impacted Areas

- `funcional/Sprints.md`
- `funcional/BackLog.md`
- `openspec/changes/rebaseline-hardening-status/`
- `openspec/specs/`
