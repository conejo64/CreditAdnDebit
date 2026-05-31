# Technical Design — fix-frontend-broken-features
# Capabilities: http-contracts, identity-and-access, issuer-ledger-billing
# Stack: .NET 9 (CardVault flat modular monolith) + Angular 17

## 0. GROUND TRUTH (verified by reading source, not specs)

### 0.1 Endpoint existence — RESOLVED
Read `backend/services/CardVault/src/CardVault.Api/Controllers/IssuerController.cs` (102 lines) directly.
The `IssuerController` exposes ONLY these card endpoints:
- `GET  api/issuer/cards`
- `POST api/issuer/cards/issue`
- `GET  api/issuer/cards/{id}`
- `POST api/issuer/cards/{id}/activate`
- `POST api/issuer/cards/{id}/block`
- `POST api/issuer/cards/{id}/pin`

**There is NO `unblock`, NO `cancel`, NO `replace` endpoint.** The proposal's inline grep was CORRECT. The base `http-contracts` spec that lists `CardService.unblockCard() -> POST api/issuer/cards/{id}/unblock` as already ✅ is WRONG / aspirational — it does not match code. DESIGN DECISION: all three endpoints (unblock, cancel, replace) MUST be CREATED from scratch. Scope confirmed: 3 new issuer endpoints, not edits to existing ones. The base http-contracts spec entry must be corrected by this change's delta.

### 0.2 Card domain model — verified
`CardEntity` (Infrastructure.Persistence/Issuer/CardEntity.cs): Id, AccountId (+ nav Account), Bin, PanToken (token in vault, max 32), MaskedPan, ExpiryYyMm, Last4, Status, PinHash/PinRetryCount/PinBlockedUntil, CreatedOn, History.
`CardStatus` enum: Created=1, Personalized=2, Printed=3, Delivered=4, Active=5, Blocked=6, Cancelled=7, Expired=8.
`CardStatusHistoryEntity`: Id, CardId, FromStatus, ToStatus, Reason(max120), ChangedOn. This IS the existing audit trail for status changes.

### 0.3 Card issuance / PAN vault — verified (resolves Open Question 1)
`IssuerService.IssueCardAsync(accountId, bin, pan, expiryYyMm, ct)`:
- Generates a NEW token string `"tok_" + Guid[..16]`.
- Tokenizes PAN into `TokenVaultEntryEntity` (KeyId, NonceB64, CiphertextB64=dev placeholder of UTF8(pan), TagB64, MaskedPan, Bin) in `_db.TokenVault`.
- Creates `CardEntity` with `PanToken = token`, Status=Created, writes a CardStatusHistory row "issued", audit event `issuer.card.issued`.
`IssuerService.ChangeStatusAsync(cardId, to, reason, ct)`: loads card, sets Status, appends CardStatusHistory(from->to,reason), audit event `issuer.card.status_changed`. Returns null if card not found.
CONFIRMATION: every card gets its OWN vault entry and its OWN distinct PanToken on issue. So "replace = issue a new card" naturally yields a NEW, distinct PAN token under the SAME account. The proposal's default replace stance is correct and aligns with how issuance already works.

### 0.4 Identity / credential store — verified (resolves Open Question 2)
- `IdentityAppDbContext : IdentityDbContext<AppUser>` uses **SQL Server** (Program.cs:89 `UseSqlServer("SqlServerIdentity")`). Holds AspNetUsers (credential hash) + `RefreshTokens` DbSet.
- `RefreshToken` entity is the EXACT precedent: Id, UserId, TokenHash (hash only, never plaintext), ExpiresOn, CreatedOn, RevokedOn, ReplacedByTokenHash, Device, computed IsActive. Unique index on (UserId, TokenHash).
- `CardVaultDbContext` uses **Postgres** (Program.cs:88) and is the issuer/billing store. Migrations live ONLY in `CardVault.Infrastructure.Persistence/Migrations/` (Postgres). There is currently NO Migrations folder in `CardVault.Infrastructure.Identity` — the Identity DB is created via `EnsureCreated()` in dev and `Migrate()` in prod (Program.cs:204/209). This is a latent gap (see Risk R-1).
- Auth handlers (`AuthCommands.cs`) use ASP.NET `UserManager<AppUser>` for all credential ops: FindByEmailAsync, CheckPasswordAsync, CreateAsync. Refresh tokens written via `IdentityAppDbContext`.

### 0.5 Notification provider — verified (slice 3 dependency is REAL)
Grep for `INotificationProvider|IEmailSender|SendEmail` across CardVault src = NO matches. There is a `CustomerNotifications` migration/table but no email-sending abstraction. The forgot-password email dispatch has NO production transport today; `real-notification-channels` must land it. Confirms slice 3 hard dependency.

### 0.6 Frontend paths — verified (resolves Open Question 3)
- Card service: `frontend/src/app/features/issuer/cards/card.service.ts` (only `blockCard()` real; `cancelCard`/`unblock`/`replace` absent). baseUrl `${environment.apiUrl}/issuer/cards` is CORRECT (single /api).
- Card detail: `frontend/src/app/features/issuer/cards/card-detail.component.ts`. NOTE: `cancelCard()` today wrongly calls `blockCard()` (fake cancel); there is a dead "Reposición de Plástico" button with no handler; "Desbloquear y Activar" currently calls `activateCard()` (wrong — must call new `unblockCard()`).
- Auth service: `frontend/src/app/core/auth.service.ts` (no forgot/reset methods).
- Forgot-password: `frontend/src/app/features/auth/forgot-password.component.ts` — `sendResetLink()` literally sets `emailSent=true` with `// Emulación de envío de correo`, zero HttpClient.
- Routes: `frontend/src/app/app.routes.ts` — has `auth/login`, `auth/forgot-password`; NO `auth/reset-password`. Standalone-component routing, signals-based services.
- Installment service (frontend): `features/finance/installment.service.ts` baseUrl has the doubled `/api/api/billing` bug.

### 0.7 Test infra — verified
`CardVaultWebApplicationFactory` already exists (tests/CardVault.Tests/Infrastructure/) booting Api with InMemory CardVaultDbContext + InMemory IdentityAppDbContext, hosted services suppressed, NullEventBus, and a `GenerateJwt(roles, extraClaims)` helper. This is the harness for integration tests of all new endpoints (auth + 403 cases) — no new harness needed.
Rate limiter already wired: `AddRateLimiter` with named policy `vault_detokenize` (Program.cs:163) and `app.UseRateLimiter()` (412). Mirror this for an `auth_password_reset` named policy.

---

## 1. Architecture Approach

CONSTRAINT (locked): CardVault is a FLAT modular monolith. Follow the EXISTING vertical-slice pattern (Controllers -> MediatR Command/Handler -> Service -> EF DbContext -> AuditService). Do NOT use the empty `CardVault.Domain` / `CardVault.Application` stub projects (each only has Class1.cs). Mirror the conventions already proven in `IssuerCommands.cs` and `AuthCommands.cs`.

Layering / placement (per locked decision):
- Entities -> `CardVault.Infrastructure.Persistence/` (Postgres) OR `CardVault.Infrastructure.Identity/Auth/` (SQL Server) — see decision ADR-2.
- Issuer commands/handlers -> `CardVault.Api/Features/Issuer/Commands/` (append to or alongside `IssuerCommands.cs`).
- Auth commands/handlers -> `CardVault.Api/Features/Auth/Commands/` (alongside `AuthCommands.cs`).
- Services -> `CardVault.Api/Services/` (`PasswordResetService` + `IPasswordResetService`).
- Controllers -> append actions to existing `IssuerController` and `AuthController`.
- Domain audit: reuse `CardStatusHistoryEntity` + `AuditService.WriteAsync` already used by IssuerService; "domain events" named in the spec (`CardUnblockedEvent`, etc.) are realized as named `AuditService.WriteAsync` event names (e.g. `issuer.card.unblocked`) + history rows. No new event-bus topic is required (NullEventBus in tests confirms event bus is incidental, not the audit source of truth).

---

## 2. Component & Data-Flow Design

### 2.1 Card lifecycle (capability: issuer-ledger-billing, http-contracts)

New `IssuerService` methods (extend existing service; reuse ChangeStatusAsync where possible):
- `UnblockCardAsync(id, ct)`: guard card exists; if status != Blocked -> 409/Conflict semantics (handler maps); else ChangeStatusAsync(id, Active, "unblocked"); audit `issuer.card.unblocked`. Returns card or null.
- `CancelCardAsync(id, reason, ct)`: guard exists; if already Cancelled -> idempotent/Conflict per handler; else ChangeStatusAsync(id, Cancelled, reason ?? "cancelled"); audit `issuer.card.cancelled`. Terminal state.
- `ReplaceCardAsync(id, reason, ct)`: TRANSACTIONAL. Load old card (+ its vault entry for Bin/expiry source). If old.Status == Cancelled -> return a sentinel for 409. Steps inside one SaveChanges/transaction:
  1. Cancel old card: ChangeStatusAsync-style row Active/Blocked->Cancelled, reason "replaced".
  2. Issue new card under SAME old.AccountId via the existing issuance path (new vault entry, new distinct PanToken, copy Bin + ExpiryYyMm; a fresh masked/last4 may reuse old masked since PAN is dev-placeholder — acceptable, design note: new card carries its own NEW PanToken which is the security-relevant value).
  3. Write bidirectional audit linkage: old card history/audit references newCardId as successor; new card history/audit references oldCardId as predecessor. Implement linkage via AuditService.WriteAsync payloads (`issuer.card.replaced` with {oldCardId, newCardId, reason}) AND set Reason fields on the two CardStatusHistory rows to encode the link (e.g. old row Reason="replaced->{newId}", new row Reason="replacement-of:{oldId}"). The `CardReplacedEvent` required fields (oldCardId, newCardId, operatorId, timestamp, reason) map to this audit payload; operatorId from the authenticated principal (User uid/sub claim).
  Returns newCardId.

Commands/handlers in `Features/Issuer/Commands/` (new records, mirror BlockCardCommand):
- `record UnblockCardCommand(Guid Id) : IRequest<IResult>` -> handler -> NoContent (204) on success, NotFound (404), Conflict (409) if not in Blocked state per spec ILB-CL-1-S2.
- `record CancelCardCommand(Guid Id, CancelCardRequest? Request) : IRequest<IResult>` -> 204 / 404 / 409-if-already-cancelled.
- `record ReplaceCardCommand(Guid Id, ReplaceCardRequest? Request) : IRequest<IResult>` -> 201 Created with body `{ newCardId }` / 404 / 409-if-cancelled.
- Request DTOs: `record CancelCardRequest(string? Reason)`, `record ReplaceCardRequest(string? Reason)`.
operatorId: handlers read it from IHttpContextAccessor / passed principal claim (uid || NameIdentifier || sub), consistent with AuthController.Me pattern.

Controller (append to `IssuerController`, all already under `[Authorize(Policy="CanOperateIssuer")]` — 403 for non-issuer roles is automatic, satisfies ILB-CL-1-S5):
- `[HttpPost("cards/{id:guid}/unblock")]` -> UnblockCardCommand
- `[HttpPost("cards/{id:guid}/cancel")]` -> CancelCardCommand (body optional reason)
- `[HttpPost("cards/{id:guid}/replace")]` -> ReplaceCardCommand (body optional reason)

NOTE on response codes: existing block/activate return `Results.Ok({id,status})` (200). The spec wants 204 for unblock/cancel and 201 for replace. DESIGN DECISION: honor the spec contract (204/201) for the NEW endpoints; do NOT retrofit existing block/activate (out of scope, regression-only).

### 2.2 Card lifecycle frontend (capability: http-contracts, issuer-ledger-billing)

`card.service.ts` add three methods (mirror existing post style):
- `unblockCard(id): Observable<void>` -> POST `${baseUrl}/${id}/unblock` (204)
- `cancelCard(id, reason?): Observable<void>` -> POST `${baseUrl}/${id}/cancel` body `{reason}` (204)
- `replaceCard(id, reason?): Observable<{newCardId:string}>` -> POST `${baseUrl}/${id}/replace` body `{reason}` (201)

`card-detail.component.ts` corrections (these are the broken surfaces to fix):
- "Desbloquear y Activar" button (toggleBlock else-branch) MUST call `unblockCard()` not `activateCard()`.
- `cancelCard()` MUST call the real `cancelCard(id)` not `blockCard(id)`. Keep confirm() prompt.
- Wire the dead "Reposición de Plástico" button to a `replaceCard()` handler with confirmation; on success, navigate to/refresh the new card id (`{newCardId}`) and toast success.
- All three actions role-gated for Admin/Operator (route already gated; buttons may additionally check `auth.hasAnyRole`/`hasPermission`).

### 2.3 Password recovery backend (capability: identity-and-access, http-contracts)

Entity `PasswordResetToken` (placement per ADR-2 -> SQL Server / IdentityAppDbContext, mirroring RefreshToken):
- Fields: Id (Guid), UserId (string, required), TokenHash (string SHA-256 of raw token, required, never store plaintext), ExpiresOn (DateTimeOffset, default now+60min), CreatedOn, UsedOn (DateTimeOffset? — single-use marker), computed `IsActive => UsedOn is null && DateTimeOffset.UtcNow < ExpiresOn`.
- EF config in `IdentityAppDbContext.OnModelCreating`: ToTable("PasswordResetTokens"), HasKey(Id), UserId required, TokenHash required, HasIndex(TokenHash) unique. Add `DbSet<PasswordResetToken> PasswordResetTokens`.

`IPasswordResetService` + `PasswordResetService` (in `CardVault.Api/Services/`):
- `Task GenerateAndDispatchAsync(string email, ct)`: FindByEmailAsync; if user exists -> generate 32-byte CSPRNG token (RandomNumberGenerator.GetBytes(32)), URL-safe Base64 encode raw for the link, store SHA-256(raw) as TokenHash + ExpiresOn=now+60min keyed to user.Id; dispatch reset email via notification provider abstraction (slice-3 dependency; see ADR-4 fallback). ALWAYS return without revealing existence (enumeration-safe; caller returns 202 unconditionally). Match timing by always doing equivalent work / a dummy hash when user is null (mitigate timing oracle).
- `Task<ResetOutcome> ResetAsync(string rawToken, string newPassword, ct)`: hash raw -> lookup TokenHash; if not found OR expired OR UsedOn!=null -> Invalid (400). Validate newPassword via UserManager password validators BEFORE applying; if policy fails -> PolicyViolation (400) and do NOT consume token (spec IAM-PR-3-S4). On success: UserManager.RemovePasswordAsync + AddPasswordAsync (or GeneratePasswordResetTokenAsync+ResetPasswordAsync identity flow), mark token UsedOn=now, revoke all user's active RefreshTokens (set RevokedOn), SaveChanges. Returns Success (204).

Commands/handlers in `Features/Auth/Commands/` (mirror AuthCommands):
- `record ForgotPasswordCommand(ForgotPasswordRequest Request) : IRequest<IResult>` -> calls service -> ALWAYS Results.Accepted (202) with empty/generic body.
- `record ResetPasswordCommand(ResetPasswordRequest Request) : IRequest<IResult>` -> service -> 204 on Success, 400 on Invalid/PolicyViolation.
- DTOs (add to Contracts): `record ForgotPasswordRequest(string Email)`, `record ResetPasswordRequest(string Token, string NewPassword)`.

Controller (append to `AuthController`):
- `[HttpPost("forgot-password")] [AllowAnonymous] [EnableRateLimiting("auth_password_reset")]` -> ForgotPasswordCommand
- `[HttpPost("reset-password")] [AllowAnonymous] [EnableRateLimiting("auth_password_reset")]` -> ResetPasswordCommand
Add named rate-limit policy `auth_password_reset` in Program.cs (mirror `vault_detokenize`, partition by client IP, modest PermitLimit e.g. 5/min).

### 2.4 Password recovery frontend (capability: identity-and-access)

`auth.service.ts` add:
- `forgotPassword(email): Observable<void>` -> POST `${apiUrl}/auth/forgot-password` `{email}`.
- `resetPassword(token, newPassword): Observable<void>` -> POST `${apiUrl}/auth/reset-password` `{token,newPassword}`.
(Plain HttpClient calls; no session mutation.)

`forgot-password.component.ts` rewrite `sendResetLink()`:
- Inject AuthService; call `forgotPassword(email)`; set `emailSent=true` ONLY in the `next` (2xx) callback; set an error state in `error`. Remove the `// Emulación` line and the Router-only stub.

New `reset-password.component.ts` (`features/auth/`) + route `auth/reset-password` in app.routes.ts:
- Read `?token=` via ActivatedRoute queryParams; if absent -> show invalid-link error (IAM-PR-4-S3).
- Form: newPassword (+ confirm), submit -> `resetPassword(token,newPassword)`; on 204 show success + link to login; on 4xx show error. Standalone component, FormsModule, matches existing auth styling.

---

## 3. ADR-style Decisions

### ADR-1: Three card lifecycle endpoints are CREATED, not edited
Decision: Create unblock/cancel/replace endpoints + commands + handlers + IssuerService methods. Rationale: source verification (IssuerController.cs) proves they do not exist; base spec was aspirational. Rejected alternative: "amend existing endpoints" — impossible, nothing to amend. Consequence: this change's http-contracts delta must CORRECT the base spec entry that marked unblock as ✅.

### ADR-2: PasswordResetToken lives in IdentityAppDbContext (SQL Server), NOT CardVaultDbContext (Postgres)
Decision: Place `PasswordResetToken` entity + EF config + DbSet in `CardVault.Infrastructure.Identity/Auth/`, mapped by `IdentityAppDbContext`. Rationale: (a) the credential it resets lives in ASP.NET Identity (AspNetUsers, SQL Server); (b) `RefreshToken` already establishes the exact precedent (auth-lifecycle token, hash-only, in the Identity context); (c) keeping reset tokens beside the credential store avoids a cross-database (Postgres<->SQL Server) join/consistency problem during reset. Rejected alternative: CardVaultDbContext/Postgres — would split an atomic credential operation across two databases and two transaction scopes with no benefit. Consequence: an Identity-context migration is required (see ADR-3).

### ADR-3: Identity migration strategy — name `AddPasswordResetTokens`
Decision: Create the FIRST EF migration for `IdentityAppDbContext`, named `AddPasswordResetTokens`, in a new `CardVault.Infrastructure.Identity/Migrations/` folder (DbContext-specific, SQL Server provider). Rationale: prod path calls `idDb.Database.Migrate()` (Program.cs:209); adding a table only via model change would NOT be picked up by Migrate() in prod, and tests use InMemory (schema-agnostic) so the gap would hide. Caveat/Risk R-1: the Identity DB currently has NO migrations (created via EnsureCreated in dev). Generating the first migration produces a baseline that includes the full Identity schema (AspNetUsers, RefreshTokens, etc.). The migration must be reviewed so it represents the true current SQL Server schema (or generated against an empty DB and applied to fresh environments). Rejected alternative: rely on EnsureCreated only — fails the prod Migrate() path and is non-auditable. Migration name: `AddPasswordResetTokens` (or `InitialIdentitySchema` if tooling forces a baseline first; document whichever the tooling emits).

### ADR-4: Notification dispatch decoupled behind an interface with a staging stub fallback
Decision: PasswordResetService depends on a notification abstraction (the `real-notification-channels` provider). Until that lands, register a no-op/log-only stub implementation behind a feature flag/config so slices 1+2 are unblocked and slice 3 backend can be unit/integration tested by asserting the dispatch was INVOKED (mock), not that an email left the building. Rationale: hard dependency confirmed (no INotificationProvider in source today). Rejected alternative: inline SMTP in this change — out of scope, owned by real-notification-channels. Consequence: slice-3 end-to-end email acceptance is gated on the dependency; the 202/204 contract + token lifecycle are fully testable now via the stub.

### ADR-5: "Domain events" realized via AuditService + CardStatusHistory, not a new Kafka topic
Decision: CardUnblockedEvent/CardCancelledEvent/CardReplacedEvent are implemented as named `AuditService.WriteAsync` events (`issuer.card.unblocked|cancelled|replaced`) plus CardStatusHistory rows; replace linkage encoded bidirectionally in audit payload + history Reason. Rationale: matches the existing pattern (IssuerService already emits `issuer.card.status_changed`); tests run with NullEventBus, so the event bus is not the audit source of truth. Rejected alternative: introduce new event-bus contracts — unnecessary scope, untestable in current harness. Consequence: "event emitted" assertions in tests check audit writes / history rows.

### ADR-6: Honor new spec response codes (204/201), do not retrofit existing endpoints
Decision: unblock/cancel return 204, replace returns 201+{newCardId}; existing block/activate keep their current 200 shape. Rationale: spec HC-2 + ILB-CL-1 mandate these for the NEW endpoints; retrofitting block/activate is out of scope and risks frontend regressions. Rejected alternative: make everything 204 for consistency — scope creep + breaks existing card-detail success handlers.

---

## 4. Slice Boundaries (Chained PRs — busts 400-line budget)

The change exceeds the 400-line review budget. Three autonomous, independently shippable slices:

### Slice 1 — Installments URL fix (independent, trivial)
Scope: `installment.service.ts` baseUrl `/api/billing` -> `/billing`; add frontend test asserting URL has no `/api/api/` and hits `<apiUrl>/billing/...`. Start: branch off main. Finish: getPlans/deferPurchase resolve 200. Rollback: revert one-line change. No backend, no migration. Smallest, ship first.

### Slice 2 — Card lifecycle (backend + frontend + tests; no external dep)
Scope: 3 IssuerService methods + 3 commands/handlers + 3 controller actions + DTOs; card.service.ts 3 methods; card-detail buttons (fix unblock, fix cancel, wire replace). Tests: handler unit tests (unblock/cancel/replace incl. 409 paths), integration tests via CardVaultWebApplicationFactory (204/201/403/404/409), frontend service tests for 3 methods. Start: after slice 1 or parallel (no overlap). Finish: all four lifecycle actions work end-to-end with audit. Rollback: revert controller actions + frontend methods (additive). No migration.

### Slice 3 — Forgot/Reset password (backend + frontend + tests; DEPENDS on real-notification-channels)
Scope: PasswordResetToken entity + IdentityAppDbContext config + DbSet + `AddPasswordResetTokens` migration; IPasswordResetService/PasswordResetService; 2 commands/handlers + DTOs; AuthController 2 endpoints + `auth_password_reset` rate-limit policy; auth.service.ts 2 methods; forgot-password rewrite; new reset-password component + route. Tests: PasswordResetService unit (CSPRNG entropy, SHA-256 hash-only storage, expiry, single-use, policy-violation-does-not-consume), integration (202 known/unknown enumeration-safe, 204 valid, 400 expired/reused), component tests (forgot HTTP wire-up + success-only-on-2xx; reset token-presence validation + 204 success). Dependency: end-to-end email gated on real-notification-channels; ship behind notification stub (ADR-4) if it slips — backend contract + token lifecycle fully verifiable via mock dispatch. Start: after slice 2 (or parallel; disjoint files). Finish: real recovery path. Rollback: additive — revert auth additions + drop migration.

Recommended order: 1 -> 2 -> 3. Slices 1 and 2 have NO dependency and can land immediately; slice 3 lands when the notification dependency is ready (or with the stub).

---

## 5. Test Strategy (Strict TDD)

- Backend runner: `dotnet test backend/CardSwitchPlatform.sln` (full solution). Per-project: CardVault.Tests.
- Frontend runner: `npm --prefix frontend test`.
- Backend test layers: (1) handler unit tests with the service mocked/in-memory for command->result mapping incl. error codes; (2) integration tests via existing `CardVaultWebApplicationFactory` (InMemory both DBs, JWT minting via GenerateJwt) for endpoint contracts, auth (403 for non-CanOperateIssuer), and enumeration-safety. PasswordResetService unit tests assert token entropy, hash-only persistence, expiry, single-use, policy-violation-non-consumption.
- Frontend: HttpTestingController to assert exact URLs/bodies/methods; component tests for success-only-on-2xx, error states, token-presence validation.
- TDD loop: write failing test (red) -> minimal impl (green) -> refactor, per slice, per behavior.

## 6. Risks
- R-1 (HIGH): Identity DB has NO existing migrations (EnsureCreated in dev, Migrate in prod). First Identity migration must faithfully baseline the SQL Server schema or prod Migrate() will diverge from EnsureCreated-created DBs. Requires careful review of the generated migration against the live schema.
- R-2 (MED): real-notification-channels not merged -> slice 3 end-to-end email blocked; mitigated by ADR-4 stub + mock-based verification.
- R-3 (MED): Replace transaction must be atomic across cancel-old + issue-new + dual audit; ensure single SaveChanges/transaction so a partial replace cannot leave a cancelled card with no successor.
- R-4 (LOW): Frontend card-detail currently mislabels actions (cancel calls block, unblock calls activate); fixing semantics may change observed UX behavior — covered by component tests.
- R-5 (LOW): New endpoints return 204/201 while existing return 200; frontend must not assume a body for unblock/cancel.
