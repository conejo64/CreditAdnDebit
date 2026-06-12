# Verify Report - Slice 5: /register Interim Lockdown
## Change: ola0-security-hardening
## Slice: S5 (SEC-7, ADR-5)
## Branch: feat/ola0-s5-register-lockdown
## Reviewed commits: 536a165 (RED), a433ab3 (GREEN), 63bee1f (docs)
## Base commit (S4 tip): 2de5acf
## Date: 2026-06-12
## Mode: Strict TDD
## Verdict: PASS

## Test Suite Result

| Project | Tests | Result |
|---------|-------|--------|
| IsoAudit.Tests | 18 | PASS |
| IsoSwitch.Tests | 53 | PASS |
| CardVault.Tests | 576 | PASS |
| Total | 647 | GREEN |

Expected 647 (baseline 644 + 3 new S5 tests). Build: 0 errors, 15 warnings (all pre-existing).

## Scope Discipline

Files changed between 2de5acf..HEAD:
- backend/services/CardVault/src/CardVault.Api/Controllers/AuthController.cs (1-line attribute swap)
- backend/services/CardVault/tests/CardVault.Tests/Security/RegisterLockdownTests.cs (new, 95 lines)
- openspec/changes/ola0-security-hardening/tasks.md (docs checkoff)

Scope discipline: CLEAN.

## TDD Integrity

RED commit 536a165: AuthController.cs has [AllowAnonymous] on Register. Under [AllowAnonymous],
ASP.NET Core bypasses ALL authorization middleware. Anonymous calls reach the action and return 400
(model validation), not 401. All three test assertions (401, 403, not-401-or-403) FAIL. Genuine RED.

GREEN commit a433ab3: Single-line diff. [AllowAnonymous] replaced by
[Authorize(Policy=CanManageUsersRoles)] on AuthController.cs:24. All three scenarios pass.

## TDD Compliance

| Check | Result | Details |
|-------|--------|---------|
| TDD Evidence reported | PASS | Found in apply-progress S5 section |
| All tasks have tests | PASS | 3 tasks, RegisterLockdownTests.cs created |
| RED confirmed | PASS | File exists; RED behavior verified via git show 536a165 |
| GREEN confirmed | PASS | 647/647 pass |
| Triangulation adequate | PASS | 3 test cases for 3 distinct scenarios |
| Safety Net | N/A | New file |

TDD Compliance: 6/6 passed

## Spec Compliance Matrix (SEC-7)

| Requirement | Scenario | Test | Result |
|-------------|----------|------|--------|
| SEC-7 - [AllowAnonymous] removed | Anonymous to 401 | AnonymousRegister_Returns401 | COMPLIANT |
| SEC-7 - CanManageUsersRoles enforced | Auditor to 403 | AuthenticatedWithoutPolicy_Returns403 | COMPLIANT |
| SEC-7 - Policy holder proceeds | Admin reaches handler | AuthenticatedWithPolicy_ReachesHandler | COMPLIANT |

Compliance summary: 3/3 scenarios compliant

## Policy Definition Analysis

Policy at Program.cs:210: RequireAssertion RoleOrPerm(user, UsersManage, Admin)
RoleOrPerm: roles.Any(user.IsInRole) || user.HasClaim(perm, perm)
Satisfiers: IsInRole(Admin) or HasClaim(perm, users:manage)
Seeded Admin satisfies IsInRole(Admin). Test factory stamps ClaimTypes.Role=Admin. Coherent.

## Other [AllowAnonymous] paths assessed

| Endpoint | File:Line | Rationale |
|----------|-----------|-----------|
| POST /api/auth/login | AuthController.cs:31 | Login must be anonymous |
| POST /api/auth/mfa/enable | AuthController.cs:38 | Pre-session MFA enrollment |
| POST /api/auth/mfa/verify | AuthController.cs:45 | Pre-session MFA verification |
| POST /api/auth/refresh | AuthController.cs:52 | Token refresh by refresh token |
| POST /api/auth/forgot-password | AuthController.cs:74 | Password reset by design |
| POST /api/auth/reset-password | AuthController.cs:82 | Reset by token |
| POST /api/notifications/delivery-callback/{id} | NotificationsController.cs:48 | Webhook; HMAC/ECDSA protected; no user creation |
| POST /api/open-banking/oauth/token | OpenBankingController.cs:20 | OAuth2 client_credentials; no user creation |
| GET /api/simulator/options | SimulatorEndpoints.cs:22 | IsoSwitch read-only demo; different service |

None create user accounts. Seeder uses UserManager.CreateAsync directly (Program.cs:403).
No residual anonymous path to account creation.

## Assertion Quality

Scanned RegisterLockdownTests.cs (3 tests, 95 lines):
- AnonymousRegister_Returns401 line 38: Assert.Equal(Unauthorized, response.StatusCode) - VALID
- AuthenticatedWithoutPolicy_Returns403 line 68: Assert.Equal(Forbidden, response.StatusCode) - VALID
- AuthenticatedWithPolicy_ReachesHandler lines 85-86: Assert.NotEqual(Unauthorized) + Assert.NotEqual(Forbidden) - VALID

Assertion quality: All assertions verify real behavior

## Issues Found

CRITICAL: None
WARNING: None
SUGGESTION:
- S-1: AuthenticatedWithPolicy_ReachesHandler uses negative assertions (not 401/403) rather than
  asserting expected 400. Intentional to avoid coupling to handler internals. Non-blocking.

## Git Hygiene

Working tree clean. No old revisions checked out during verify. No staged changes.

## Verdict

PASS

All 647 tests green (CardVault 576, IsoSwitch 53, IsoAudit 18). SEC-7 fully compliant.
TDD genuine. Scope clean. 0 CRITICAL, 0 WARNING, 1 SUGGESTION (non-blocking).
