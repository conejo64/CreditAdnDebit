# Technical Design — Secure User Registration via Admin Invitation
# Change: secure-user-registration
# Capability: identity-and-access

This design is the HOW at architectural level. It supersedes the file-placement claims in the proposal where they conflict with the locked flat-monolith constraint and with the verified codebase reality (see ADR-0 below).

---

## 0. Codebase Reality Check (verified, not assumed)

1. **CardVault is a FLAT modular monolith.** `RegisterUserCommand` and all auth handlers live in `CardVault.Api/Features/Auth/Commands/AuthCommands.cs` (record + handler co-located). Controllers are thin and only `_mediator.Send(...)`. Entities live under `CardVault.Infrastructure.Persistence/<Area>/`. The empty `CardVault.Domain` / `CardVault.Application` stub projects are being deleted. The proposal's `CardVault.Domain/Auth/...` and `CardVault.Application/Features/Auth/...` paths are WRONG and MUST NOT be used.

2. **TWO DbContexts on TWO DIFFERENT DATABASE PROVIDERS** (Program.cs:88-89):
   - `CardVaultDbContext` -> **PostgreSQL**. Owns business entities + transactional **Outbox** (`OutboxMessages`) + `AuditEvents`.
   - `IdentityAppDbContext` -> **SQL Server**. Owns ASP.NET Identity (`AppUser`, roles, claims) and `RefreshTokens`.
   - CONSEQUENCE: user creation (SQL Server) and outbox/audit (Postgres) CANNOT share one ACID transaction. This single fact dominates the design (ADR-2, ADR-4).

3. **Transactional outbox is the canonical durable-event pattern** (Loyalty/Accounting/Notification/Wallet/CreditLimit/Catalog/RoutingRule all use it). `EfOutboxPublisher` drains `CardVaultDbContext.OutboxMessages` to Kafka with retry (Attempts<10) + LastError.

4. **`PciAuditPublisher` is fire-and-forget** (direct to Kafka, NOT durable). NOT used for invitation audit.

5. **Policy `CanManageUsersRoles`** already exists (Program.cs:143) = role `Admin` OR claim `perm=users:manage`. No new permission.

6. **Seeding uses `UserManager<AppUser>` DIRECTLY** (Program.cs:233-273), NOT the HTTP `/register` route. So locking `/register` does NOT break seeding (resolves Open Q1).

7. **No `ForwardedHeaders` middleware** registered; `Connection.RemoteIpAddress` used raw. Capturing real client IP behind LB needs `UseForwardedHeaders` (resolves Open Q5).

8. **v76/v77 reference pattern**: thin controller, `[Authorize(Policy=...)]`, `User.Identity?.Name` for actor, MediatR command returns id, EF config in `OnModelCreating`, dedicated migration. This is the template.

---

## 1. Architecture Approach

Vertical slice over a flat modular monolith. Feature folder `CardVault.Api/Features/Auth/Invitations/` with MediatR commands + handlers. Thin controller methods on the existing `AuthController`. One new persistence entity in the Postgres-backed `CardVaultDbContext`. No new layers, no new projects.

**Boundary decision (CRITICAL):** Invitation lifecycle (state + audit) owned by `CardVaultDbContext` (Postgres). User account materialization owned by `IdentityAppDbContext` (SQL Server) via existing `UserManager<AppUser>` / `RegisterUserCommand`. The accept flow orchestrates ACROSS the two stores in a deliberate, ordered, compensatable sequence (no distributed transaction available — see ADR-4).

### Component map
```
HTTP (AuthController, thin)
  POST   /api/auth/register            [Authorize(CanManageUsersRoles)]  (was AllowAnonymous)
  POST   /api/auth/invitations         [Authorize(CanManageUsersRoles)]  -> IssueUserInvitationCommand
  DELETE /api/auth/invitations/{id}    [Authorize(CanManageUsersRoles)]  -> RevokeUserInvitationCommand
  POST   /api/auth/accept-invite       [AllowAnonymous, token-gated]     -> AcceptUserInvitationCommand
        |
        v
MediatR Features/Auth/Invitations/
  IssueUserInvitationCommandHandler   --> CardVaultDbContext (Postgres): insert UserInvitationEntity + OutboxMessage(UserInvitationIssued)
                                          + OutboxMessage(notification.user-invitation.requested)  [1 txn]
                                      --> returns plaintext token ONCE + metadata
  AcceptUserInvitationCommandHandler  --> 1) CardVaultDbContext: atomic claim Pending->Accepted (optimistic concurrency) + Accepted-audit outbox
                                      --> 2) IdentityAppDbContext: RegisterUserCommand (UserManager.CreateAsync + AddToRoles)
                                      --> on failure after claim: compensation + Rejected(ProvisioningFailed)
  RevokeUserInvitationCommandHandler  --> CardVaultDbContext: Pending->Revoked + OutboxMessage(UserInvitationRevoked)  [1 txn]
        |
        v
Infrastructure
  CardVault.Infrastructure.Persistence/Auth/UserInvitationEntity.cs   (NEW entity)
  CardVaultDbContext: DbSet + OnModelCreating config   (MODIFIED)
  Migration AddUserInvitations (Postgres)   (NEW)
  EfOutboxPublisher (UNCHANGED) drains the outbox rows to Kafka
```

---

## 2. UserInvitationEntity — schema, EF config, migration

File: `CardVault.Infrastructure.Persistence/Auth/UserInvitationEntity.cs` (NEW). Lives with the outbox in Postgres — NOT in the Identity context.

```csharp
public enum UserInvitationStatus { Pending = 0, Accepted = 1, Expired = 2, Revoked = 3 }

public sealed class UserInvitationEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = default!;
    public string RolesCsv { get; set; } = default!;        // pre-declared roles, comma-separated
    public string TokenHash { get; set; } = default!;        // base64 of SHA-256(rawTokenBytes); NEVER plaintext
    public DateTimeOffset IssuedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string IssuedByUserId { get; set; } = default!;
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public UserInvitationStatus Status { get; set; } = UserInvitationStatus.Pending;
    public uint Version { get; set; }                        // xmin concurrency token (Postgres) — ADR-3
}
```

EF config in `CardVaultDbContext.OnModelCreating`: ToTable("UserInvitations"), HasKey(Id), Email(256) required, RolesCsv(512) required, TokenHash(64) required, IssuedByUserId(450) required, unique HasIndex(TokenHash), HasIndex(Email, Status), Version IsRowVersion (Postgres xmin). DbSet added to context. **Migration:** `AddUserInvitations` (Postgres). Dev uses EnsureCreated; tests use SQLite/in-memory factory.

---

## 3. Token generation / hashing
- Generation: `RandomNumberGenerator.GetBytes(32)` (256 bits).
- Transport: base64url (no padding) of raw bytes -> plaintext returned once to admin, shipped to invitee.
- Storage: `Convert.ToBase64String(SHA256.HashData(rawBytes))` in `TokenHash`. DB NEVER holds plaintext.
- Log hygiene: `token` field excluded from logs; integration test asserts plaintext never appears in captured log output.

---

## 4. State machine
```
(none) --issue--> Pending --accept(valid)--> Accepted [terminal]
                  Pending --accept(expired)--> Expired [terminal] (lazy: read-time check)
                  Pending --admin DELETE--> Revoked [terminal]
```
`Expired` enforced on read (ExpiresAtUtc < now => 410 even if Status still Pending); no sweeper required for correctness. Only `Pending` is mutable; transition from terminal on accept -> 409.

---

## 5. Command/handler structure & RegisterUserCommand reuse
All under `CardVault.Api/Features/Auth/Invitations/`. `RegisterUserCommand` stays an internal application primitive; the accept handler calls `await _mediator.Send(new RegisterUserCommand(...), ct)` and then assigns pre-declared roles via `UserManager.AddToRolesAsync` (mirroring seeding loop Program.cs:262-265). The `/register` route stops being anonymous but the command is unchanged and reachable in-process.

---

## 6. Concurrency control for single-use
Optimistic concurrency via Postgres `xmin` row-version (ADR-3). Accept = load Pending -> set Accepted -> SaveChanges. Concurrent loser gets `DbUpdateConcurrencyException` -> 409. Satisfies "Concurrent acceptance — exactly one succeeds" without table locks.

---

## 7. Notification hand-off (to real-notification-channels)
At issuance, a SECOND outbox row `Topic="cv.notification.user-invitation.requested"`, `Key=invitationId`, `PayloadJson={email, token, expiresAtUtc}` is published by the EXISTING `EfOutboxPublisher`. ZERO hard runtime dependency: if no consumer exists, the message waits on Kafka and the admin has the token in the API response as fallback. The plaintext token in this transient broker payload is the one acceptable exception to hash-only-at-rest; it is NOT persisted in `UserInvitations`.

---

## 8. ADRs

### ADR-0 — Flat monolith placement, NOT Domain/Application
Entity -> `Infrastructure.Persistence/Auth/`; commands/handlers -> `Api/Features/Auth/Invitations/`. The Domain/Application projects are empty stubs being deleted. Matches v76/v77 locked pattern.

### ADR-1 — `/register` is POLICY-GATED, not physically removed (Open Q1)
Remove `[AllowAnonymous]`, add `[Authorize(Policy="CanManageUsersRoles")]`. Route + command remain. Seeding unaffected (direct UserManager). Rejected: deleting the route — spec says protect, and an Admin-gated direct-create is a legitimate audited capability.

### ADR-2 — Audit durability via transactional OUTBOX, NOT PciAuditPublisher (Open Q2)
All audit events written as `OutboxMessageEntity` rows in the SAME `SaveChangesAsync` as the state change (atomic for single-store issue/revoke). If the outbox insert fails, the whole SaveChanges rolls back — no state change without audit. Matches the canonical codebase pattern; PciAuditPublisher is fire-and-forget (not durable) and rejected for this purpose.

### ADR-3 — Single-use via optimistic concurrency (Postgres xmin)
Map `Version` to xmin (IsRowVersion). Lock-free, exactly-one-winner by the DB. Rejected: `SELECT ... FOR UPDATE` (holds a tx across the cross-store user-creation call). Fallback: conditional `UPDATE ... WHERE Status=Pending` if xmin mapping is troublesome in tests.

### ADR-4 — Cross-store accept: claim-first ordering with compensation (the hard problem)
No shared transaction across Postgres+SQL Server. ORDER: [Postgres txn: claim Pending->Accepted + write Accepted-audit outbox] -> [SQL Server: create user + assign roles].
- If claim loses race -> 409, no user created. Safe.
- If user-create fails AFTER claim -> COMPENSATE: revert invitation to Pending (or Failed) + emit `UserInvitationRejected(ProvisioningFailed)`. If compensation also fails, a rare orphan (Accepted invitation with no AppUser) is detectable by an ops query. Accept this residual risk rather than introduce 2PC/Saga infra for a low-frequency admin flow.
Rejected: (a) create-user-first then claim (race -> two users for one token); (b) 2PC/TransactionScope across Npgsql+SqlServer (fragile/unsupported); (c) unify Identity into Postgres (massive scope creep).

### ADR-5 — Revocation endpoint IS in scope (Open Q3)
`DELETE /api/auth/invitations/{id}` (`CanManageUsersRoles`). Pending->Revoked + NEW audit event `UserInvitationRevoked`. Revoke of non-Pending -> 409/404. The spec's endpoint table lists it; a revoked token must be unusable + audited. This is a 4th audit event (spec named 3) — flag to spec owner.

### ADR-6 — Constant-time comparison: SHOULD, effectively N/A here (Open Q4)
Token lookup is by INDEXED `SHA-256(token)` equality in the DB (not app-level byte comparison), so no app-exposed timing side-channel on the secret. Uniform `400` for BOTH malformed and unknown tokens closes the structural-vs-unknown oracle. `FixedTimeEquals` only required IF comparison ever moves into app memory (it never does here).

### ADR-7 — CallerIp behind a load balancer (Open Q5)
Register `UseForwardedHeaders` (XForwardedFor|XForwardedProto) EARLY, with `KnownProxies`/`KnownNetworks` restricted to the trusted LB. Controller captures `RemoteIpAddress` into `AcceptUserInvitationCommand.CallerIp` -> stored in `UserInvitationRejected` payload. SECURITY: must restrict to known proxies or attackers forge the audited IP.

---

## 9. Integration points / config
- `Program.cs`: add `app.UseForwardedHeaders(...)` before `UseAuthentication`; new config `Invitations: { ExpiryHours: 48 }` with a startup guard rejecting `> 72` (spec TTL ceiling); `ForwardedHeaders: { KnownProxies: [...] }`.
- No change to `IdentityAppDbContext` schema. No new permission (reuses `users:manage`).

---

## 10. Test strategy (Strict TDD — `dotnet test backend/CardSwitchPlatform.sln`)
Tests under `tests/CardVault.Tests/Features/Auth/`. RED-GREEN-REFACTOR:
1. Unit token/service: 32-byte CSPRNG; base64url round-trip; SHA-256 storage != plaintext; default TTL 48h; ceiling 73 -> clamped/rejected to 72.
2. Unit state machine: Pending->Accepted; expired -> 410; Accepted/Revoked -> 409.
3. Unit concurrency: two accepts -> one success, one DbUpdateConcurrencyException -> 409.
4. Integration authz: anonymous /register -> 401; non-users:manage -> 403 (register, invitations POST, invitations DELETE).
5. Integration happy path: issue (201 + token once + Pending + Issued outbox); accept (201, user created in Identity, roles assigned, Accepted outbox, AcceptedAtUtc set).
6. Integration negatives: expired 410 + Rejected(Expired); reused 409 + Rejected(AlreadyUsed); malformed/unknown 400 + Rejected(InvalidToken) IDENTICAL response (ADR-6).
7. Integration revoke (ADR-5): revoke Pending success + Revoked audit; accept revoked -> 409; revoke Accepted -> 409.
8. Integration log hygiene: plaintext token never in captured logs.
9. Integration audit durability (ADR-2): state change + audit outbox committed together.
10. Integration CallerIp (ADR-7): X-Forwarded-For from known proxy -> Rejected payload carries forwarded client IP.

NOTE for apply: confirm the test factory provides a writable Identity store for the cross-store accept path (ADR-4); if it only wires Postgres/SQLite, add an in-memory/SQLite Identity context for accept integration tests.

---

## 11. Risks
- R1 (HIGH): cross-store accept (ADR-4) bounded inconsistency window (audited-Accepted but user-create failed); mitigated by compensation + Rejected(ProvisioningFailed) + ops detection query. No 2PC. apply/verify MUST test the compensation path.
- R2 (MED): xmin/IsRowVersion mapping must be verified against actual Npgsql in tests; fallback conditional UPDATE.
- R3 (MED): test factory may not wire SQL Server Identity context; accept integration tests need a writable Identity store.
- R4 (LOW): `UseForwardedHeaders` without KnownProxies in non-dev is a spoofing hole; make it required config.
- R5 (LOW): roles assigned in accept handler post user-create; partial AddToRolesAsync failure -> user without full roles; surface as handler error + audit.
- R6 (scope): `UserInvitationRevoked` is a 4th audit event the spec didn't name; flag to spec owner.

---

## 12. Scope confirmations (answers to the 5 open questions)
1. `/register` stays, becomes `CanManageUsersRoles`-gated; seeding unaffected. [ADR-1]
2. Audit via transactional outbox; rollback on audit-write failure for single-store ops; cross-store accept anchors audit to the Postgres claim txn with compensation. [ADR-2, ADR-4]
3. Revocation endpoint IS in scope, with `UserInvitationRevoked` audit + tests. [ADR-5]
4. Constant-time = SHOULD/N-A: indexed hash lookup + uniform 400 removes the oracle. [ADR-6]
5. CallerIp via `UseForwardedHeaders` restricted to KnownProxies. [ADR-7]
