# Verification Report: harden-isoswitch-access

**Date**: 2026-05-25  
**Project**: ZitronSystem  
**Change**: `harden-isoswitch-access`  
**Mode**: Strict TDD / hybrid artifact store  
**Verdict**: PASS WITH WARNINGS  
**Archive Ready**: Yes

## Executive Summary

The final verification passed. The full solution test suite was executed with `dotnet test backend/CardSwitchPlatform.sln --logger "console;verbosity=normal"` and completed with 165 passing tests: 128 CardVault tests and 37 IsoSwitch tests.

All four added spec scenarios are backed by passing authorization-boundary tests and/or accepted code-inspection evidence. HTTP/JWT WebApplicationFactory coverage remains intentionally deferred because `IsoSwitch.Api` startup performs PostgreSQL migration work and would require integration infrastructure outside this slice.

## Completeness

| Metric | Value |
|--------|-------|
| Tasks total | 5 |
| Tasks complete | 5 |
| Tasks incomplete | 0 |
| Spec scenarios evaluated | 4 |
| Scenarios covered | 4 |

## Build & Test Execution

**Command**

```text
dotnet test backend/CardSwitchPlatform.sln --logger "console;verbosity=normal"
```

**Result**: Passed

```text
CardVault.Tests: 128 total, 128 passed, 0 failed
IsoSwitch.Tests: 37 total, 37 passed, 0 failed
Overall: 165 total, 165 passed, 0 failed
```

**Observed warnings**

```text
NU1902: OpenTelemetry.Exporter.OpenTelemetryProtocol 1.9.0 has a known moderate severity vulnerability.
Fluent Assertions commercial-license notice emitted during IsoSwitch tests.
```

## TDD Compliance

| Check | Result | Details |
|-------|--------|---------|
| TDD evidence reported | ✅ | Apply-progress evidence was provided for RED/GREEN and inspected against the repository. |
| Test file exists | ✅ | `backend/services/IsoSwitch/tests/IsoSwitch.Tests/Auth/AuthBoundaryTests.cs` exists. |
| RED confirmed | ✅ | Auth-boundary tests are present for anonymous, wrong-role, role, and permission paths. |
| GREEN confirmed | ✅ | Full suite passed; IsoSwitch auth-boundary tests passed at runtime. |
| Triangulation adequate | ✅ | Four policies are exercised across deny and allow variants. Actual file contains 17 `[Fact]` tests. |
| Safety net | ✅ | Existing solution suite was executed and passed. |

**TDD Compliance**: 6/6 checks passed.

## Test Layer Distribution

| Layer | Tests | Files | Notes |
|-------|-------|-------|-------|
| Unit / policy-boundary | 17 | 1 | `AuthBoundaryTests.cs` validates `IAuthorizationService` policy behavior directly. |
| HTTP integration | 0 | 0 | Deferred by accepted scope because `IsoSwitch.Api` starts PostgreSQL migrations on host startup. |
| E2E | 0 | 0 | Out of scope for this slice. |
| **Total new auth-boundary tests** | **17** | **1** | All passed. |

## Changed File Coverage

Coverage analysis was skipped — no coverage command/tool was provided for this verify phase. This is informational and non-blocking.

## Assertion Quality

**Assertion quality**: ✅ All assertions verify real authorization behavior. The tests call production authorization services and assert `Succeeded` true/false outcomes; no tautologies or smoke-only assertions were found.

## Spec Scenario Coverage

| Requirement | Scenario | Evidence | Result |
|-------------|----------|----------|--------|
| Protected IsoSwitch Operational Access | Switch monitor and audit access require an authenticated role | Passing tests: `ViewSwitchMonitor_AnonymousUser_IsDenied`, `ViewSwitchMonitor_Operator_IsDenied`, `ViewSwitchMonitor_Auditor_IsAllowed`, `ViewSwitchMonitor_GranularPermission_IsAllowed`, `ViewAudit_AnonymousUser_IsDenied`, `ViewAudit_Operator_IsDenied`, `ViewAudit_Auditor_IsAllowed`, `ViewAudit_GranularPermission_IsAllowed`. Static evidence: `TransactionQueriesEndpoints.cs` and `AuditEndpoints.cs` use `RequireAuthorization(...)`. | ✅ COVERED |
| Protected IsoSwitch Operational Access | Switch execution endpoints require operator or admin authority | Passing tests: `OperateSwitch_AnonymousUser_IsDenied`, `OperateSwitch_Auditor_IsDenied`, `OperateSwitch_Operator_IsAllowed`, `OperateSwitch_Admin_IsAllowed`, `OperateSwitch_GranularPermission_IsAllowed`. Static evidence: `TransactionEndpoints.cs` maps `/api/iso` through `RequireAuthorization(IsoSwitchAuthorizationPolicies.OperateSwitch)`. | ✅ COVERED |
| Explicit Separation Between Operational And Demo Endpoints | Operational ISO processing does not rely on anonymous demo routes | Static evidence: `Program.cs` maps transaction, query, catalog, simulator, and audit endpoint groups separately; operational groups use authorization policies in `TransactionEndpoints.cs`, `TransactionQueriesEndpoints.cs`, `CatalogEndpoints.cs`, `AuditEndpoints.cs`, and protected simulator operations. Passing policy tests prove anonymous access is denied for the relevant policies. | ✅ COVERED |
| Explicit Separation Between Operational And Demo Endpoints | Demo helpers remain clearly identifiable | Static evidence: anonymous helper routes are isolated in `SimulatorEndpoints.cs` under explicit helper paths such as `/simulator/options`, `/demo/*`, and `/tcp/status`, while operational simulator actions use protected groups. `Program.cs` also isolates the development ISO simulator hosted service behind the Development environment condition. | ✅ COVERED |

**Compliance summary**: 4/4 scenarios covered.

## Correctness / Static Evidence

| Area | Status | Notes |
|------|--------|-------|
| JWT authentication registration | ✅ | `Program.cs` configures JWT bearer validation with issuer, audience, signing key, and lifetime validation. |
| Authorization policies | ✅ | `CanOperateSwitch`, `CanViewSwitchMonitor`, `CanManageSwitchRoutes`, and `CanViewAudit` are configured and tested. |
| Endpoint protection | ✅ | Operational endpoint groups use explicit `RequireAuthorization(...)` policies. |
| Permission catalog alignment | ✅ | CardVault `PermissionCatalog` includes `switch:operate`, `switch:monitor`, `routing:manage`, and `audit:view`. |
| Demo/helper separation | ✅ | Anonymous helper routes are limited to identifiable demo/helper paths; operational actions remain protected. |

## Issues

### CRITICAL

None.

### WARNING

- HTTP/JWT WebApplicationFactory tests are deferred. This is accepted for this slice because `IsoSwitch.Api` startup applies PostgreSQL migrations and would require non-trivial integration infrastructure.
- Downstream handler behavior is not exhaustively covered by this change; the new tests focus on the authorization boundary, which is the changed contract.
- `NU1902` remains for `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.9.0 in CardVault and IsoSwitch projects.
- `AuthBoundaryTests.cs` contains 17 tests, while the provided apply-progress summary says 16 auth-boundary tests. This is a documentation/count mismatch only; the actual coverage is stronger than reported.
- `UnitTest1.cs` still exists as a two-line placeholder namespace file, although it contains no tests. This is non-blocking but can be cleaned in a maintenance pass.

### SUGGESTION

- Add HTTP-level auth integration tests in a future quality slice with a test-host seam for database migrations or a PostgreSQL test container.

## Final Verdict

**PASS WITH WARNINGS**

The implementation is archive-ready. All spec scenarios have accepted runtime or code-inspection evidence, and the full solution test suite passes with 165/165 tests successful.
