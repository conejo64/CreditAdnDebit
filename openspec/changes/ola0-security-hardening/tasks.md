# Tasks: Ola 0 — Security Hardening Quick Wins
## Change: `ola0-security-hardening`
## Generated: 2026-06-08
## Artifact store: hybrid (Engram + openspec)

---

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~920 prod + ~680 test ≈ 1,600 total |
| 400-line budget risk per slice | S1: Med · S2: Low · S3: Med · S4: Low · S5: Low · S6: Med |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (S1) → PR 2 (S2) → PR 3 (S3) → PR 4 (S4) → PR 5 (S5) → PR 6 (S6) |
| Delivery strategy | auto-chain |
| Chain strategy | stacked-to-main |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: Medium

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| S1 | Secrets + startup validation (3 services) | PR 1 | Base = main; ~500 lines; highest blast radius, ship first |
| S2 | IsoAudit JWT hardening | PR 2 | Base = PR 1 merged; ~120 lines; depends on S1 IsoAudit JwtOptions |
| S3 | PAN/ISO log leakage (TcpIsoClient) | PR 3 | Base = PR 2 merged; ~200 lines incl. AllowInvalidCert ADR-7 |
| S4 | CORS allowlist (3 services) | PR 4 | Base = PR 3 merged; ~160 lines; config only |
| S5 | /register interim lockdown | PR 5 | Base = PR 4 merged; ~80 lines; trivial but policy-critical |
| S6 | Statement-recalc dedup | PR 6 | Base = PR 5 merged; ~540 lines; characterization gate required |

---

## Slice 1 — Secrets + Startup Validation

**Goal**: All three services fail-fast on missing/DEV/short secrets. CI can provision env vars
before Slice 1 lands. Spec refs: SEC-1, SEC-2, SEC-3.

### Task 1.1 — CI/dev secret provisioning scaffold (PREREQUISITE — do first)
- [ ] Create `.env.example` at repo root listing required keys per service:
  - `Jwt__SigningKey` (CardVault, ≥32 chars)
  - `Tokenization__Secret` (IsoSwitch, ≥32 chars)
  - `Jwt__Key` (IsoAudit, ≥32 chars, mapped to `Jwt:Key`)
- [ ] Add README note (or `docs/secrets.md`) explaining `dotnet user-secrets set` workflow for dev and CI env-var pattern
- [ ] Verify CI pipeline can set those env vars BEFORE `dotnet test` runs (document in README; do NOT gate merge on pipeline green until S1 tests are merged — this task just prepares the scaffold)
- **Spec ref**: SEC-1/2/3 risk mitigation; ADR-1 (#1 risk)

### Task 1.2 — Write failing tests: host refuses to start on missing/DEV secret (RED)
- [ ] Create `CardVault.Tests/Security/StartupSecretValidationTests.cs`:
  - `CardVault_MissingJwtSigningKey_ThrowsOnStart`
  - `CardVault_DevPlaceholderSigningKey_ThrowsOnStart`
  - `CardVault_ValidSigningKey_StartsSuccessfully`
  - Use `WebApplicationFactory<CardVault.Api.Program>` overriding config; assert host build throws `OptionsValidationException`
- [ ] Create `IsoSwitch.Tests/Security/StartupSecretValidationTests.cs`:
  - `IsoSwitch_MissingTokenizationSecret_ThrowsOnStart`
  - `IsoSwitch_DevPlaceholderSecret_ThrowsOnStart`
  - `IsoSwitch_ValidTokenizationSecret_StartsSuccessfully`
- [ ] Create `IsoAudit.Tests/Security/StartupSecretValidationTests.cs`:
  - `IsoAudit_MissingJwtKey_ThrowsOnStart`
  - `IsoAudit_DevPlaceholderJwtKey_ThrowsOnStart`
  - `IsoAudit_ValidJwtKey_StartsSuccessfully`
- **Spec ref**: SEC-1 scenarios, SEC-2 scenarios, SEC-3 scenarios

### Task 1.3 — Write failing tests: validator unit matrix (RED)
- [ ] Create `CardVault.Tests/Security/JwtOptionsValidatorTests.cs`:
  - Empty string → Fail; 31-char → Fail; `DEV_ONLY_change_me_please_32+chars` → Fail; valid 32+ random → Success
- [ ] Create `IsoSwitch.Tests/Security/TokenizationOptionsValidatorTests.cs`:
  - Empty → Fail; 31-char → Fail; `DEV_ONLY_CHANGE_ME` → Fail; `CHANGE_ME_...` → Fail; valid 32+ → Success
- [ ] Create `IsoAudit.Tests/Security/JwtOptionsValidatorTests.cs`:
  - Same matrix for `DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM` placeholder
- **Spec ref**: ADR-1 validator matrix

### Task 1.4 — Create `TokenizationOptions` + `IsoAudit` `JwtOptions` (GREEN foundation)
- [ ] Create `IsoSwitch.Api/Security/TokenizationOptions.cs`:
  ```csharp
  public sealed class TokenizationOptions
  {
      public const string Section = "Tokenization";
      [Required, MinLength(32)]
      public string Secret { get; set; } = string.Empty;
  }
  ```
- [ ] Create `IsoAudit.Api/Security/JwtOptions.cs` (minimal: `Key`, `Issuer`, `Audience` properties; no DEV default)
- **Spec ref**: ADR-1

### Task 1.5 — Create `IValidateOptions<T>` validators for all 3 secrets (GREEN)
- [ ] Create `CardVault.Api/Security/JwtOptionsValidator.cs` — rejects `Forbidden` substrings (`DEV_ONLY`, `CHANGE_ME`, `change_me`, `placeholder`) and length < 32
- [ ] Create `IsoSwitch.Api/Security/TokenizationOptionsValidator.cs` — same pattern for `TokenizationOptions.Secret`
- [ ] Create `IsoAudit.Api/Security/JwtOptionsValidator.cs` — same pattern for `IsoAudit` `JwtOptions.Key`
- **Spec ref**: ADR-1

### Task 1.6 — Wire options + ValidateOnStart in all 3 `Program.cs` files (GREEN)
- [ ] `CardVault.Api/Program.cs`: remove DEV default from `JwtOptions.SigningKey` (set `= string.Empty`); add:
  ```csharp
  builder.Services.AddOptions<JwtOptions>().BindConfiguration("Jwt")
      .ValidateDataAnnotations().ValidateOnStart();
  builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
  ```
- [ ] `IsoSwitch.Api/Program.cs`: wire `TokenizationOptions` + validator; remove `??` in `TokenPanService` registration
- [ ] `IsoAudit.Api/Program.cs`: wire `JwtOptions` + validator; remove inline `?? "DEV_ONLY..."` at line 23
- **Spec ref**: ADR-1

### Task 1.7 — Update `TokenPanService` to inject `IOptions<TokenizationOptions>` (GREEN)
- [ ] Modify `IsoSwitch.Api/Security/TokenPanService.cs`: change ctor from `IConfiguration` to `IOptions<TokenizationOptions>`; remove `?? "DEV_ONLY_CHANGE_ME"` null-coalesce
- **Spec ref**: ADR-1, Codebase Reality #2

### Task 1.8 — Clean committed placeholder secrets from `appsettings*.json` (GREEN)
- [ ] `CardVault.Api/appsettings.json` / `appsettings.Development.json`: remove `Jwt:SigningKey` placeholder value (leave key absent or empty comment)
- [ ] `IsoSwitch.Api/appsettings.json`: remove `Tokenization:Secret` placeholder; leave `Tokenization:{}` section skeleton
- [ ] `IsoAudit.Api/appsettings.json`: remove `Jwt:Key` placeholder
- **Spec ref**: ADR-1 (#1 risk; placeholder removal is non-negotiable)

### Task 1.9 — Verify GREEN: all S1 tests pass, baseline preserved
- [ ] Run `dotnet test backend/CardSwitchPlatform.sln`; confirm ≥596 + new S1 tests green
- **Spec ref**: Success criterion — no DEV literal remains; host refuses to start

---

## Slice 2 — IsoAudit JWT Hardening

**Goal**: IsoAudit validates issuer/audience/lifetime and gates HTTPS metadata on environment. Depends on S1 (`JwtOptions` for IsoAudit exists). Spec ref: SEC-4.

**CRITICAL COORDINATION**: Before writing any JWT validation code, read `CardVault.Api/Services/TokenService.cs` to find the EXACT `Issuer` and `Audience` strings it stamps into tokens. Pin those values in `IsoAudit/appsettings.json` as `Jwt:Issuer` and `Jwt:Audience`. If `TokenService` uses a config-driven value, trace it to `CardVault/appsettings.json`. Wrong values here will cause `ValidateIssuer/Audience=true` to reject every legitimate token.

### Task 2.1 — Write failing tests: wrong issuer/audience → 401 and HTTPS metadata gating (RED)
- [x] Create `IsoAudit.Tests/Security/JwtHardeningTests.cs`:
  - `WrongIssuer_Returns401` — issue token with `issuer="wrong-issuer"` (valid key, valid audience) → `GET /api/audit/logs` → 401
  - `WrongAudience_Returns401` — issue token with `audience="wrong-audience"` → 401
  - `DevelopmentEnv_RequireHttpsMetadata_IsFalse` — assert `TokenValidationParameters.RequireHttpsMetadata == false` when env=Development
  - `ProductionEnv_RequireHttpsMetadata_IsTrue` — assert true when env=Production
- **Spec ref**: SEC-4 all scenarios

### Task 2.2 — Read `CardVault` `TokenService` issuer/audience + pin config (GREEN foundation)
- [x] Open `CardVault.Api/Services/TokenService.cs` and record the exact `Issuer` and `Audience` values used in `JwtSecurityToken` construction
- [x] Add `Jwt:Issuer` and `Jwt:Audience` entries to `IsoAudit.Api/appsettings.json` matching those exact values
- [x] Add `Jwt:Issuer` and `Jwt:Audience` to `.env.example` for IsoAudit
- **Spec ref**: ADR-2; cross-service coordination gap flagged in design
- **Verified**: CardVault stamps issuer="CardVault", audience="CardSwitch" (from JwtOptions defaults in appsettings.json; TokenService reads _opt.Issuer/_opt.Audience)

### Task 2.3 — Update `IsoAudit.Api/Program.cs` JWT validation params (GREEN)
- [x] Replace inline `Jwt:Key` read with `IOptions<JwtOptions>` (from S1); set:
  ```csharp
  ValidateIssuer = true,
  ValidIssuer = jwtOpts.Issuer,
  ValidateAudience = true,
  ValidAudience = jwtOpts.Audience,
  ValidateLifetime = true,
  RequireHttpsMetadata = !builder.Environment.IsDevelopment()
  ```
- [x] Remove stale `?? "DEV_ONLY..."` if any remains from S1 (should be clean)
- **Also fixed**: W-2 from S1 verify — IssuerSigningKey now comes from IOptions<JwtOptions> via AddOptions<JwtBearerOptions>().Configure<IOptions<JwtOptions>, IHostEnvironment>() PostConfigure pattern (true DI, evaluated after app.Build()).
- **Also fixed**: DB init now guards MigrateAsync with provider-name check (ProviderName.Contains("InMemory")) so test environments using Production env name work correctly.
- **Spec ref**: ADR-2; SEC-4

### Task 2.4 — Verify GREEN: S2 tests pass, S1 tests still green
- [x] Run `dotnet test backend/CardSwitchPlatform.sln`; confirm ≥596 + S1 + S2 tests green
- **Result**: 636 total — IsoAudit: 16 (+4 new), IsoSwitch: 49, CardVault: 571. All pass.
- **Spec ref**: SEC-4 success criteria

---

## Slice 3 — PAN/ISO Log Leakage (TcpIsoClient)

**Goal**: No cardholder data or raw ISO bytes reach any log sink. Fold in `AllowInvalidCert=false` outside Dev (ADR-7). Spec ref: SEC-5.

### Task 3.1 — Write failing tests: failing exchange contains MTI only, no PAN/hex/Base64 (RED)
- [ ] Create `IsoSwitch.Tests/Net/TcpIsoClientLoggingTests.cs`:
  - `SendFailure_LogContainsMti_NotBase64Payload` — drive `SendAsync` to fail (TCP unavailable); capture `ILogger<TcpIsoClient>` via test sink; assert log message contains MTI string; assert no Base64 pattern (`[A-Za-z0-9+/]{20,}={0,2}`) in log entries
  - `ReceiveFailure_LogContainsMti_NotHexBytes` — same for receive path; assert no hex pattern (`[0-9A-Fa-f]{20,}`) in log entries
  - Use a capturing `ILogger` test double (e.g., `Microsoft.Extensions.Logging.Testing.FakeLogger` or custom collector)
- **Spec ref**: SEC-5 scenarios

### Task 3.2 — Add `ILogger<TcpIsoClient>` ctor parameter + DI factory update (GREEN)
- [ ] Modify `IsoSwitch.Infrastructure.SwitchIso8583/Net/TcpIsoClient.cs`:
  - Add `ILogger<TcpIsoClient> logger` parameter to primary ctor
  - Replace `Console.WriteLine(Convert.ToBase64String(...))` at line 69 with `_logger.LogWarning("ISO exchange failed mti={Mti} trace={TraceId}", request.Mti, ...)` — no payload bytes
  - Replace `Console.WriteLine(Convert.ToHexString(respPayload))` at line 112 with `_logger.LogWarning("ISO receive failed mti={Mti}", ...)` — no hex bytes
- [ ] Modify `IsoSwitch.Api/Program.cs` DI factory (lines 117-124):
  ```csharp
  builder.Services.AddSingleton(sp => new TcpIsoClient(
      BuildOptions(sp.GetRequiredService<IConfiguration>()),
      sp.GetRequiredService<ILogger<TcpIsoClient>>()));
  ```
- **Spec ref**: SEC-5; ADR-3

### Task 3.3 — Update `SimulatorConnector` and `TcpGatewayConnector` to pass logger (GREEN)
- [ ] Modify `IsoSwitch.Infrastructure.SwitchIso8583/Connectors/SimulatorConnector.cs`: add `ILogger<TcpIsoClient>` to ctor; forward to `TcpIsoClient` construction
- [ ] Modify `IsoSwitch.Infrastructure.SwitchIso8583/Connectors/TcpGatewayConnector.cs`: same
- **Spec ref**: ADR-3; Codebase Reality #3 (both connectors `new` TcpIsoClient directly)

### Task 3.4 — Fold in `AllowInvalidCert=false` outside Development (ADR-7)
- [ ] In the DI factory from 3.2, set `AllowInvalidCert = builder.Environment.IsDevelopment() && configuredValue`
- [ ] Remove `AllowInvalidCert=true` from `IsoSwitch.Api/appsettings.json` (or gate it behind `appsettings.Development.json`)
- **Spec ref**: ADR-7 (P2 fold-in); Codebase Reality #4

### Task 3.5 — Verify GREEN: S3 tests pass, S1+S2 still green
- [ ] Run `dotnet test backend/CardSwitchPlatform.sln`; confirm no Console.WriteLine PAN/hex paths remain
- **Spec ref**: SEC-5 success criteria

---

## Slice 4 — CORS Allowlist

**Goal**: Replace `AllowAnyOrigin()` in all 3 services with config-driven `WithOrigins(...)`. Spec ref: SEC-6.

### Task 4.1 — Write failing tests: non-allowlisted origin blocked, allowlisted echoed (RED)
- [ ] Create `CardVault.Tests/Security/CorsAllowlistTests.cs`:
  - `EvilOrigin_NoCorsHeader_Returned` — CORS preflight with `Origin: https://evil.example.com`; assert response has NO `Access-Control-Allow-Origin`
  - `AllowlistedOrigin_CorsHeader_Returned` — preflight with configured origin; assert `Access-Control-Allow-Origin: <origin>`
  - Use `WebApplicationFactory<CardVault.Api.Program>` with `Cors:AllowedOrigins:0 = https://allowed.example.com`
- [ ] Repeat for `IsoSwitch.Tests/Security/CorsAllowlistTests.cs` and `IsoAudit.Tests/Security/CorsAllowlistTests.cs`
- **Spec ref**: SEC-6 scenarios

### Task 4.2 — Replace `AllowAnyOrigin()` in all 3 `Program.cs` files (GREEN)
- [ ] `CardVault.Api/Program.cs` (line 52): replace CORS registration:
  ```csharp
  var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
  builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
      p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
  ```
- [ ] `IsoSwitch.Api/Program.cs` (line 48): same pattern
- [ ] `IsoAudit.Api/Program.cs` (line 11): same pattern
- [ ] `app.UseCors()` calls remain unchanged
- **Spec ref**: ADR-4

### Task 4.3 — Add `Cors:AllowedOrigins` to `appsettings.json` for all 3 services (GREEN)
- [ ] `CardVault.Api/appsettings.json`: add `"Cors": { "AllowedOrigins": [] }` (empty; dev overrides via user-secrets or `appsettings.Development.json`)
- [ ] `IsoSwitch.Api/appsettings.json`: same
- [ ] `IsoAudit.Api/appsettings.json`: same
- [ ] Add `Cors__AllowedOrigins__0` to `.env.example`
- **Spec ref**: ADR-4

### Task 4.4 — Verify GREEN: S4 tests pass, S1–S3 still green
- [ ] Run `dotnet test backend/CardSwitchPlatform.sln`
- **Spec ref**: SEC-6 success criteria

---

## Slice 5 — `/register` Interim Lockdown

**Goal**: Anonymous `POST /api/auth/register` returns 401; non-Admin returns 403. INTERIM only — no invitation logic. Spec ref: SEC-7.

### Task 5.1 — Write failing tests: anonymous 401, non-policy 403, policy proceeds (RED)
- [ ] Create `CardVault.Tests/Security/RegisterLockdownTests.cs`:
  - `AnonymousRegister_Returns401` — no `Authorization` header → 401; assert no user created in DB
  - `AuthenticatedWithoutPolicy_Returns403` — valid JWT for `Auditor` role (no `users:manage` claim) → 403; assert no user created
  - `AuthenticatedWithPolicy_ReachesHandler` — valid JWT for `Admin` → response is NOT 401/403 (may be 400 on invalid payload, that's fine)
  - Use `WebApplicationFactory<CardVault.Api.Program>` with JWT test token helper
- **Spec ref**: SEC-7 scenarios

### Task 5.2 — Swap `[AllowAnonymous]` for `[Authorize]` on `Register` action (GREEN)
- [ ] Modify `CardVault.Api/Controllers/AuthController.cs` line 24:
  - Remove: `[AllowAnonymous]`
  - Add: `[Authorize(Policy = "CanManageUsersRoles")]`
  - No other changes; no invitation logic; no new policy
- **Spec ref**: ADR-5; SEC-7

### Task 5.3 — Verify GREEN: S5 tests pass, S1–S4 still green, baseline preserved
- [ ] Run `dotnet test backend/CardSwitchPlatform.sln`; confirm 596+ green
- **Spec ref**: SEC-7 success criteria

---

## Slice 6 — Statement-Recalc Dedup

**Goal**: Single `ApplyClosingTotals` method on `BillingService`; both consumer and generator use it. CHARACTERIZATION TEST MUST PASS BEFORE ANY REFACTOR. Spec ref: SEC-8.

### Task 6.1 — Write characterization tests pinning current totals (RED — GATE)
- [ ] Create `CardVault.Tests/Billing/StatementTotalsCharacterizationTests.cs`:
  - `BillingService_GenerateStatement_ProducesExpectedTotals`: given PreviousBalance=100, Purchases=200, Payments=-50, Fees=10, Interest=5 → assert `TotalPaymentDue == 265m` and `NewBalance == 265m` via the BillingService path (call into actual `GenerateStatementAsync` or extract the totaling logic under test)
  - `SwitchTxnConsumer_UpdateOpenStatement_ProducesExpectedTotals`: same inputs via the consumer path → assert identical `TotalPaymentDue == 265m` and `NewBalance == 265m`
  - Both tests MUST be RED (currently duplicated code — tests pin behavior before any refactor)
  - **DO NOT extract `ApplyClosingTotals` until these tests pass against the ORIGINAL code**
- **Spec ref**: SEC-8 characterization scenario; ADR-6

### Task 6.2 — Extract `ApplyClosingTotals` on `BillingService` (GREEN)
- [ ] Add `internal void ApplyClosingTotals(StatementEntity st)` to `CardVault.Api/Services/BillingService.cs`:
  ```csharp
  internal void ApplyClosingTotals(StatementEntity st)
  {
      st.InterestDue = st.InterestAccrued;
      st.FeesDue = st.Fees;
      st.PrincipalDue = Math.Max(0, st.NewBalance - st.InterestDue - st.FeesDue);
      st.TotalPaymentDue = st.PrincipalDue + st.InterestDue + st.FeesDue;
      st.NewBalance = st.TotalPaymentDue;
  }
  ```
- [ ] Replace lines 123–131 in `BillingService.GenerateStatementAsync` with `ApplyClosingTotals(st)` call
- **Spec ref**: ADR-6

### Task 6.3 — Update `SwitchTxnConsumer` to call `ApplyClosingTotals` (GREEN)
- [ ] Modify `CardVault.Api/Background/SwitchTxnConsumer.cs` `UpdateOpenStatementAsync` (lines 344-398):
  - Before calling the terminal formula: set `st.NewBalance = computedBalance` (consumer's cycle-aggregation result)
  - Resolve `BillingService` from the existing scope (SwitchTxnConsumer.cs:334)
  - Call `billingService.ApplyClosingTotals(st)` instead of duplicated lines 385-393
  - **Do NOT call `GenerateStatementAsync`** — must NOT create a new statement
- **Spec ref**: ADR-6 seam note

### Task 6.4 — Write convergence assertion tests (REFACTOR verification)
- [ ] Add to `StatementTotalsCharacterizationTests.cs`:
  - `BothPaths_ProduceIdenticalTotals_ForSameInputs` — drive both paths with identical inputs; assert `TotalPaymentDue` and `NewBalance` are equal across paths
  - Verify tests were RED in 6.1 and are now GREEN after 6.2+6.3 with zero formula changes
- **Spec ref**: SEC-8 characterization scenario

### Task 6.5 — Verify GREEN: S6 tests pass, full suite still green
- [ ] Run `dotnet test backend/CardSwitchPlatform.sln`; confirm ≥596 + all new S1–S6 tests green
- [ ] Confirm no `Console.WriteLine`, no `DEV_ONLY` literals, no `AllowAnyOrigin`, no `[AllowAnonymous]` on Register remain in codebase
- **Spec ref**: All success criteria (proposal §Success Criteria)

---

## Cross-Slice Testability Constraints

Apply to ALL test code in this change:
- `WebApplicationFactory<Program>` override pattern for host-refuses-to-start tests — DO NOT call a real host; assert the exception at `factory.Services` or `CreateClient()` call
- Capturing `ILogger` test double (in-memory collector, not Serilog) for log-leakage tests — never assert against stdout
- JWT test tokens: use `System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler` with the test key; do NOT hardcode real keys in test files
- Characterization tests (S6) MUST be committed and green against ORIGINAL code before the refactor commit lands

---

## Dependency Graph

```
S1 (secrets + startup) → S2 (IsoAudit JWT) → S3 (log leakage + AllowInvalidCert)
                                                           ↓
                                                    S4 (CORS)
                                                           ↓
                                                    S5 (/register)
                                                           ↓
                                                    S6 (statement dedup)
```

S2 depends on S1 (IsoAudit `JwtOptions` created in S1). S3–S6 are independent of each other but are sequenced for clean auto-chain PR stack. S3, S4, S5 could technically run in parallel with separate branches; under `stacked-to-main` they stay sequential for a clean linear history.
