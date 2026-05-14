# Proposal: Harden IsoSwitch Access

## Why

`IsoSwitch.Api` currently exposes operational endpoints without authentication or authorization. That leaves ISO transaction execution, switch monitoring, audit queries, and routing or catalog administration reachable without a validated CardVault session, which conflicts with the project hardening gates and the documented role model.

## Scope

- configure `IsoSwitch.Api` to validate the same JWT bearer tokens issued by `CardVault.Api`
- enforce policy-based authorization on switch operational endpoints
- keep demo and diagnostic helpers explicitly separated from the protected operational ISO workflows
- align permission naming so switch operation access can be granted consistently from CardVault identity management

## Out Of Scope

- new business capabilities from `v76+`
- broad frontend redesigns
- expanding the automated test suite beyond the existing hardening implementation itself

## Impacted Areas

- `backend/services/IsoSwitch/src/IsoSwitch.Api`
- `backend/services/CardVault/src/CardVault.Api`
- `backend/README.md`
- `openspec/specs/identity-and-access`
- `openspec/specs/iso-switch-processing`
