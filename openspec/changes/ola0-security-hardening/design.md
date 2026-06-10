# Design: Ola 0 — Security Hardening Quick Wins
# Change: ola0-security-hardening

This is the HOW at architectural level. Six independent slices (proposal order), each its own commit/PR (auto-chain). Verified against main @128b64c.

## 0. Codebase Reality Check (verified)
1. **Two `JwtOptions` exist**: `CardVault.Api/Security/JwtOptions.cs` (mutable `set;`, DEV default in `SigningKey`) and `IsoSwitch.Api/Security/JwtOptions.cs` (`init;`, empty default). IsoAudit has NO options class — reads `Jwt:Key` inline (Program.cs:23) with a `?? "DEV_ONLY..."`.
2. **IsoSwitch tokenization** is a separate secret: `TokenPanService(IConfiguration)` reads `Tokenization:Secret` with `?? "DEV_ONLY_CHANGE_ME"` (TokenPanService.cs:17). Registered as **singleton** (Program.cs:50).
3. **`TcpIsoClient` has NO logger and is NOT logger-injectable today**: registered via `AddSingleton(sp => new TcpIsoClient(host,port,timeout))` (Program.cs:117-124) and also `new`ed inside `SimulatorConnector`/`TcpGatewayConnector` (Program.cs:127-128). Leaks via `Console.WriteLine`: Base64 of full request (TcpIsoClient.cs:69) and full hex of response (:112). The full ISO frame contains DE2 PAN / DE35 Track2 / DE52 PIN block.
4. **`AllowInvalidCert`** is already a `TcpIsoClientOptions` flag honored at TcpIsoClient.cs:130; today the DI factory never sets it (defaults false), but appsettings can.
5. **`CanManageUsersRoles` policy EXISTS** (CardVault Program.cs:201 = `UsersManage` perm OR `Admin`). `/register` is `[AllowAnonymous]` (AuthController.cs:24).
6. **Statement totals**: `GenerateStatementAsync` (BillingService.cs:128-131) and consumer `UpdateOpenStatementAsync` (SwitchTxnConsumer.cs:390-393) both compute `PrincipalDue = max(0, balance - InterestDue - FeesDue)`, then `TotalPaymentDue = Principal+Interest+Fees`, `NewBalance = TotalPaymentDue`. The consumer is a `private static` method on the consumer, taking `(db, minPay, accountId, postedOn)` — it does NOT share BillingService dependencies. The duplicated unit is the **bucket-to-totals closing formula**, not the whole cycle-aggregation.
7. Three CORS registrations are identical `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` (CardVault:52, IsoSwitch:48, IsoAudit:11).

## 1. Technical Approach
Options + `ValidateOnStart()` for every secret; env-gated JWT hardening; ILogger injection with allow-list logging; config-driven CORS allowlist; one authz attribute; one extracted totals method. No new projects, no schema changes. Each slice is RED-GREEN-REFACTOR with `dotnet test backend/CardSwitchPlatform.sln`.

## 2. Architecture Decisions (ADRs)

### ADR-1 — Secret validation: options + `IValidateOptions<T>` rejecting DEV literals AND short keys (Slice 1)
**Choice**: For each secret-bearing options type, register:
```csharp
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration("Jwt")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
```
`SigningKey` gets `[Required, MinLength(32)]`; **remove the hard-coded DEV default** (`= "DEV_ONLY..."` → `= default!`/`string.Empty`). The custom validator rejects known dev literals so a 32-char placeholder cannot satisfy `MinLength`:
```csharp
internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private static readonly string[] Forbidden =
        { "DEV_ONLY", "CHANGE_ME", "change_me", "placeholder" };
    public ValidateOptionsResult Validate(string? name, JwtOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.SigningKey) || o.SigningKey.Length < 32)
            return ValidateOptionsResult.Fail("Jwt:SigningKey must be >= 32 chars.");
        if (Forbidden.Any(f => o.SigningKey.Contains(f, StringComparison.OrdinalIgnoreCase)))
            return ValidateOptionsResult.Fail("Jwt:SigningKey is a placeholder; set a real secret.");
        return ValidateOptionsResult.Success;
    }
}
```
New `TokenizationOptions { public const string Section = "Tokenization"; [Required, MinLength(32)] public string Secret { get; set; } = ""; }`. `TokenPanService` switches from `IConfiguration` to `IOptions<TokenizationOptions>` (still singleton). IsoAudit gains a minimal `JwtOptions`/inline `AddOptions` for `Jwt:Key` and stops using the inline `??`.
**Secret sources**: dev → **user-secrets** (`dotnet user-secrets`); CI/prod → **environment variables** (`Jwt__SigningKey`, `Tokenization__Secret`). Ship `appsettings.Development.json` with NO secret (forces user-secrets) + a `.env.example`/README block listing required keys per service. This is the #1 risk; the slice MUST include the docs + a `appsettings` cleanup removing committed placeholders.
**Alternatives rejected**: DataAnnotations only (a 32-char `DEV_ONLY...` literal passes MinLength — fails to catch placeholders); `IConfiguration` null-coalesce (current bug). **Rationale**: `ValidateOnStart` turns a silent insecure boot into a fail-fast; the custom validator closes the placeholder gap DataAnnotations cannot.

### ADR-2 — IsoAudit JWT hardening, env-gated HTTPS (Slice 2)
**Choice**: In IsoAudit Program.cs set `ValidateIssuer=true`, `ValidIssuer=<Jwt:Issuer>`, `ValidateAudience=true`, `ValidAudience=<Jwt:Audience>`, `ValidateLifetime=true`, and `RequireHttpsMetadata = !builder.Environment.IsDevelopment()`. Issuer/Audience come from the IsoAudit `Jwt` config section (added in ADR-1), matching the issuer CardVault `TokenService` stamps. **Rationale**: IsoSwitch already validates issuer/audience (Program.cs:59-65); IsoAudit is the only relaxed validator. `IHostEnvironment.IsDevelopment()` keeps local HTTP working while enforcing HTTPS metadata in CI/prod. **Rejected**: hard `RequireHttpsMetadata=true` (breaks dev), leaving issuer/audience off (token from any signer with the shared key is accepted).

### ADR-3 — Log redaction via constructor-injected `ILogger<TcpIsoClient>` (Slice 3)
**Choice**: Add `ILogger<TcpIsoClient>` as a constructor parameter on the primary `TcpIsoClient(TcpIsoClientOptions, ILogger<TcpIsoClient>)` ctor; keep the `(host,port,timeout)` convenience ctor but require it to forward a logger (overload takes `ILogger<TcpIsoClient>`), or change the DI factory to resolve the logger:
```csharp
builder.Services.AddSingleton(sp => new TcpIsoClient(
    BuildOptions(sp.GetRequiredService<IConfiguration>()),
    sp.GetRequiredService<ILogger<TcpIsoClient>>()));
```
The two connectors that `new` it (`SimulatorConnector`, `TcpGatewayConnector`) must also receive and pass the logger (they are themselves DI-resolved, so add `ILogger<TcpIsoClient>` to their ctors). Replace both `Console.WriteLine` sites: log **MTI + trace/correlation id + generic outcome** only (`_log.LogWarning(ex, "ISO exchange failed mti={Mti} trace={TraceId}", request.Mti, ...)`); **never** log the encoded frame, hex, Base64, DE2/DE35/DE52. **Rationale**: DI already reaches every construction path; structured `ILogger` honors Serilog config and removes stdout PAN leakage. **Rejected**: redaction regex over the existing strings (fragile, still constructs the secret string in memory), static logger (untestable).

### ADR-4 — CORS named allowlist policy from config (Slice 4)
**Choice**: Config section `Cors: { AllowedOrigins: ["https://app.zitron..."] }`. Each Program.cs:
```csharp
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
```
`app.UseCors()` stays. **Rationale**: minimal diff (default policy already consumed via `UseCors()`), origins externalized so dev/CI/prod differ by config. **Rejected**: keeping `AllowAnyOrigin` (defeats the slice); per-endpoint policies (out of scope). NOTE: `AllowCredentials` + `WithOrigins` is valid; `AllowAnyOrigin`+credentials is not — another reason the old config was wrong.

### ADR-5 — `/register` interim lockdown: one attribute swap (Slice 5)
**Choice**: AuthController.cs:24 — replace `[AllowAnonymous]` with `[Authorize(Policy = "CanManageUsersRoles")]`. Nothing else; no invitation logic (owned by `secure-user-registration`). Seeding uses `UserManager` directly (verified in that change's design), so unaffected. **Rationale**: policy already exists; smallest secure change. **Rejected**: deleting the route, adding invitation flow (scope creep).

### ADR-6 — Statement dedup: extract a shared internal totals method on `BillingService` (Slice 6)
**Choice**: Add to `BillingService` a single internal method that applies the **closing formula** to an already-populated statement:
```csharp
internal void ApplyClosingTotals(StatementEntity st)
{
    st.InterestDue = st.InterestAccrued;
    st.FeesDue = st.Fees;                 // caller sets Interest/Fees/buckets first
    st.PrincipalDue = Math.Max(0, st.NewBalance - st.InterestDue - st.FeesDue);
    st.TotalPaymentDue = st.PrincipalDue + st.InterestDue + st.FeesDue;
    st.NewBalance = st.TotalPaymentDue;
}
```
`GenerateStatementAsync` calls it after building buckets (replacing lines 123-131); `SwitchTxnConsumer.UpdateOpenStatementAsync` resolves `BillingService` from its scope (it already creates a scope, SwitchTxnConsumer.cs:334) and calls `ApplyClosingTotals` (replacing lines 385-393). The consumer keeps its own cycle aggregation (it sums ALL entries; generate sums only unassigned entries) — only the **terminal formula** is shared, which is exactly the duplicated/divergent logic.
**Seam note**: `GenerateStatement` derives PrincipalDue from `NewBalance` (computed at line 99 incl. installments); consumer derives from `computedBalance` then assigns to `NewBalance`. To unify, the consumer sets `st.NewBalance = computedBalance` BEFORE calling `ApplyClosingTotals`, making both paths feed the method the same `NewBalance` input. **Characterization test FIRST** (RED): pin current `TotalPaymentDue`/`NewBalance`/`PrincipalDue` for a representative cycle through BOTH paths, then refactor and assert identical outputs. **Rejected**: consumer calling `GenerateStatementAsync` (different scope: it must NOT create a new statement, only recalc the open one); duplicating into a static helper (loses BillingService cohesion).

### ADR-7 — P2 fold-in: include `AllowInvalidCert` hardening, DEFER plaintext DB password (decision)
**Choice**: **Include** forcing `AllowInvalidCert=false` outside Development inside Slice 3 (it is one line in the same `TcpIsoClient` area and is a cert-validation security flag): in the DI factory set `AllowInvalidCert = builder.Environment.IsDevelopment() && cfg-value`. **Defer** the plaintext `Password=postgres` connection-string removal: it spans connection-string sourcing across multiple services + docker/compose env wiring and is config-only (no code) — it belongs with the secret-sourcing work but its blast radius (local + CI compose) is larger than a quick win and risks breaking every service's DB boot. Flag it as P2 follow-up, do NOT block. **Rationale**: keep each slice's diff coherent and under the ~400-line budget; `AllowInvalidCert` rides Slice 3's logger/DI edit, DB password needs its own deliberate connection-secret slice.

## 3. Data Flow (secret validation, representative)
```
config (env / user-secrets)
   → AddOptions<T>().BindConfiguration().ValidateDataAnnotations().ValidateOnStart()
   → host build  ──(invalid/placeholder)──► OptionsValidationException → process exits
                 ──(valid)──► IOptions<T> injected into TokenService / TokenPanService / JwtBearer
```

## 4. File Changes
| File | Action | Slice / change |
|------|--------|----------------|
| `CardVault.Api/Security/JwtOptions.cs` | Modify | DataAnnotations, drop DEV default |
| `IsoSwitch.Api/Security/JwtOptions.cs` | Modify | DataAnnotations on SigningKey |
| `IsoSwitch.Api/Security/TokenizationOptions.cs` | Create | new options + annotations |
| `*/Security/*OptionsValidator.cs` (x3) | Create | reject placeholders + short keys |
| `CardVault/IsoSwitch/IsoAudit Program.cs` | Modify | AddOptions+ValidateOnStart; IsoAudit Jwt section |
| `IsoSwitch.Api/Security/TokenPanService.cs` | Modify | IOptions<TokenizationOptions>, drop `??` |
| `IsoAudit.Api/Program.cs` | Modify | ADR-2 TokenValidationParameters + env-gated HTTPS |
| `IsoSwitch.Infrastructure.../Net/TcpIsoClient.cs` | Modify | ILogger ctor, drop Console.WriteLine PAN/hex |
| `IsoSwitch.../Connectors/{Simulator,TcpGateway}Connector.cs` | Modify | pass ILogger |
| 3x `Program.cs` | Modify | CORS allowlist (ADR-4) |
| 3x `appsettings*.json` | Modify | remove placeholder secrets, add `Cors:AllowedOrigins`, AllowInvalidCert |
| `CardVault.Api/Controllers/AuthController.cs` | Modify | ADR-5 attribute swap |
| `CardVault.Api/Services/BillingService.cs` | Modify | add `ApplyClosingTotals`, call it |
| `CardVault.Api/Background/SwitchTxnConsumer.cs` | Modify | call `ApplyClosingTotals` |
| `README` / `.env.example` | Create/Modify | required-secret docs (ADR-1) |

## 5. Testing Strategy (Strict TDD)
| Layer | What | Approach |
|-------|------|----------|
| Integration | Host REFUSES to start on missing/DEV secret (each service) | `WebApplicationFactory<Program>` overriding config to clear/placeholder the secret; assert `CreateClient()`/host build throws `OptionsValidationException` (e.g. `Assert.Throws<OptionsValidationException>(() => factory.CreateClient())`). One test per service, plus a positive "valid secret → starts" test. |
| Unit | `JwtOptionsValidator`/`TokenizationOptionsValidator` | xUnit: empty, 31-char, `DEV_ONLY...`, valid 32+ random → Fail/Success matrix. |
| Integration | IsoAudit rejects wrong issuer/audience; HTTPS metadata required outside Dev | issue token with wrong issuer → 401; assert `RequireHttpsMetadata` true when env=Production. |
| Integration | No PAN/raw ISO in logs | inject capturing `ILogger`/Serilog sink, drive a failing exchange, assert captured output contains MTI but NOT the Base64/hex/PAN/Track2. |
| Integration | CORS rejects non-allowlisted origin | request with `Origin: https://evil`, assert no `Access-Control-Allow-Origin`; allowlisted origin echoes back. |
| Integration | authz on `/register` | anonymous → 401; authenticated without `CanManageUsersRoles` → 403; with policy → reaches handler. |
| Unit/char. | Statement totals single-source | characterization test pins current totals (RED), refactor, assert `ApplyClosingTotals` output == both legacy paths; consumer == generate for same inputs. |

## 6. Migration / Rollout
No data migration. Config rollout only: provision `Jwt__SigningKey`, `Tokenization__Secret`, `Cors__AllowedOrigins__0..n` in user-secrets (dev) and env (CI/prod) BEFORE deploying Slice 1, or services will fail-fast (intended). Each slice ships as its own auto-chain commit/PR; revert is per-slice. Secret issues hotfix via config, no code revert.

## 7. Open Questions
- [x] Where does the real secret come from? → user-secrets (dev), env vars (CI/prod); docs shipped in Slice 1. (ADR-1)
- [x] Is TcpIsoClient logger-injectable? → Yes via DI factory + connector ctors; no manual `new` outside DI remains. (ADR-3)
- [x] Does `CanManageUsersRoles` exist? → Yes, Program.cs:201. (ADR-5)
- [x] Extract vs reuse for dedup? → extract internal `ApplyClosingTotals` on BillingService; consumer resolves it from its existing scope. (ADR-6)
- [x] P2 fold-in? → include AllowInvalidCert in Slice 3; defer DB password. (ADR-7)
- None blocking.
```

