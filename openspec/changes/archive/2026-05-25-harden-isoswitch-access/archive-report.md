# Archive Report: harden-isoswitch-access

**Date**: 2026-05-25
**Change**: harden-isoswitch-access
**Archive Mode**: hybrid (OpenSpec + Engram)

## Verification Verdict

**Status**: ✅ PASS WITH WARNINGS
**Archive Readiness**: ✅ APPROVED
**Critical Issues**: None
**Warnings**: HTTP E2E tests deferred (infrastructure out of scope), NU1902 advisory

## Scope Delivered

### Tasks (5/5 Complete)

1. ✅ Add JWT bearer authentication and authorization policies to `IsoSwitch.Api` using the CardVault token contract
2. ✅ Protect operational switch, monitor, audit, routing, and catalog endpoints with explicit policies
3. ✅ Keep demo and diagnostic helpers explicitly separated from protected operational routes
4. ✅ Add a switch operation permission (`switch:operate`) assignable from CardVault identity management
5. ✅ Document the access-control change in OpenSpec deltas and backend technical notes

### Test Results

| Project | Passed | Failed |
|---------|--------|--------|
| CardVault.Tests | 128 | 0 |
| IsoSwitch.Tests | 37 | 0 |
| **Total** | **165** | **0** |

**Auth boundary tests added** (16 new in `Auth/AuthBoundaryTests.cs`):
- `CanOperateSwitch`: anonymous-deny, Auditor-deny, Operator-allow, Admin-allow, granular-perm-allow
- `CanViewSwitchMonitor`: anonymous-deny, Operator-deny, Auditor-allow, granular-perm-allow
- `CanManageSwitchRoutes`: anonymous-deny, Operator-deny, Admin-allow, granular-perm-allow
- `CanViewAudit`: anonymous-deny, Operator-deny, Auditor-allow, granular-perm-allow

## Specs Synced to Source of Truth

| Domain | Action | Requirements Added |
|--------|--------|--------------------|
| `iso-switch-processing` | UPDATED | Explicit Separation Between Operational And Demo Endpoints (2 scenarios) |
| `identity-and-access` | UPDATED | Protected IsoSwitch Operational Access (2 scenarios) |

## Warnings (Non-Blocking)

- HTTP/JWT WebApplicationFactory tests deferred — PostgreSQL + Kafka infrastructure required, out of scope for this slice
- NU1902: OpenTelemetry.Exporter.OpenTelemetryProtocol 1.9.0 moderate vulnerability (informational)
- Downstream handler branch coverage not expanded in this slice

## SDD Cycle

| Phase | Status |
|-------|--------|
| Proposal | ✅ Complete |
| Spec | ✅ Complete — deltas synced |
| Design | ✅ Not required |
| Tasks | ✅ 5/5 done |
| Apply | ✅ Complete |
| Verify | ✅ PASS WITH WARNINGS |
| **Archive** | ✅ **Complete** |
