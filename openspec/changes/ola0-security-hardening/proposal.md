# Proposal: Ola 0 — Security Hardening Quick Wins

## Intent

The full repo evaluation (`architecture/three-front-remediation-plan`, main @128b64c, 596 green) surfaced P0/P1 security defects that are exploitable today on a banking card-issuing/switch platform: DEV fallback secrets baked into code and committed `appsettings.json`, JWT validation disabled in IsoAudit, full PAN/ISO-8583 payloads leaking to stdout, wide-open CORS, and an anonymous user-registration endpoint. "Ola 0" stops the bleeding with low-effort, high-impact fixes before the larger architecture and registration waves land. One non-security correctness item is bundled because it is the same risk class: the switch consumer reimplements statement totaling and can silently diverge from `BillingService`.

**Success looks like**: no service starts with a missing/DEV secret; IsoAudit validates issuer/audience/signing key and requires HTTPS metadata (env-gated); no cardholder data or raw ISO bytes reach logs; CORS is allowlisted; `/api/auth/register` rejects anonymous callers; statement totals are computed in exactly one place. Baseline stays 596+ green, every slice TDD.

## Scope

### In Scope
- **Startup secret validation** — remove `?? "DEV_ONLY..."` fallbacks and committed placeholder keys; bind JWT/Tokenization config to options with `ValidateOnStart()` so the host refuses to boot on missing/dev secrets. Sites: `IsoSwitch TokenPanService.cs:17`, `IsoAudit Program.cs:23`, `CardVault JwtOptions.cs:7`, placeholder keys in the three `appsettings.json` + `Tokenization:Secret`.
- **Harden IsoAudit JWT** — `Program.cs:14-25`: `ValidateIssuer=true`, `ValidateAudience=true`, `RequireHttpsMetadata=true` (env-gated false for local Development only).
- **Stop PAN/ISO-hex leakage** — `TcpIsoClient.cs:69` (Base64 of full request) and `:112` (hex dump) replaced with structured `ILogger` logging safe fields only (MTI, message); never payload bytes.
- **CORS allowlist** — replace `AllowAnyOrigin` in `CardVault Program.cs:52`, `IsoSwitch Program.cs:48`, `IsoAudit Program.cs:11` with a configured origin allowlist.
- **Interim lockdown of `POST /api/auth/register`** — `AuthController.cs:24`: remove `[AllowAnonymous]`, add `[Authorize(Policy="CanManageUsersRoles")]`. **INTERIM only** — the full invitation flow is owned by the `secure-user-registration` change; this slice only closes the open door and must not implement invitation logic.
- **Kill statement-recalc duplication** — `SwitchTxnConsumer.UpdateOpenStatementAsync` (`SwitchTxnConsumer.cs:344-398`) reimplements the totaling that `BillingService` already owns (same `TotalPaymentDue`/`NewBalance` formula at `BillingService.cs:130-131`). Consumer delegates to a shared `BillingService` method instead.

### Out of Scope
- TCP ISO 8583 mTLS / IP allowlist (port 7000) — separate track, L effort, needs acquirer cert coordination.
- PIN BCrypt/Argon2id migration — later wave (needs data migration + forced PIN reset).
- Full `secure-user-registration` invitation flow (`UserInvitationEntity`, tokens, accept-invite).
- **Optional (fold in only if trivial)**: `AllowInvalidCert=true` (IsoSwitch `appsettings.json`) and plaintext DB `Password=postgres` in committed config — P2; flag, do not block.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
None at the spec/requirement level — these are configuration, logging, authorization-attribute, and refactor changes to existing behavior. If `openspec/specs/identity-and-access/spec.md` exists, the `sdd-spec` phase may add a SHALL noting `/register` is no longer anonymous, but the contractual self-registration behavior is owned by `secure-user-registration`.

## Approach

Per service, follow the skill guidance (IOptions + `ValidateOnStart`, structured `ILogger`, built-in DI/auth):
- **Secrets**: introduce/extend strongly-typed options (`JwtOptions`, `TokenizationOptions`) bound from config with DataAnnotations (`[Required]`, `MinLength(32)`) and `.ValidateDataAnnotations().ValidateOnStart()`. Delete code defaults and committed placeholders; document real secrets come from env/user-secrets/vault.
- **IsoAudit JWT**: set the three validation flags; gate `RequireHttpsMetadata=false` behind `IHostEnvironment.IsDevelopment()`.
- **Logging**: inject `ILogger<TcpIsoClient>`; log MTI + a generic failure message; drop Base64/hex dumps.
- **CORS**: read an `AllowedOrigins[]` config section; `WithOrigins(...)` instead of `AllowAnyOrigin`.
- **Register lockdown**: one-attribute swap, mirroring the existing `CanManageUsersRoles` policy.
- **Statement dedup**: extract the consumer's totaling into a reusable `BillingService` method (or have the consumer call the existing one) so there is a single source of truth.

Each slice is TDD (RED then GREEN). Startup-validation slices need a `WebApplicationFactory`-style test asserting the host throws on missing/DEV secret.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `IsoSwitch.Api/Security/TokenPanService.cs`, `IsoSwitch.Api/Security/JwtOptions.cs`, `CardVault.Api/Security/JwtOptions.cs` | Modified | Remove DEV fallbacks; bind via validated options |
| 3x `appsettings.json` (CardVault/IsoSwitch/IsoAudit) + `Tokenization:Secret` | Modified | Remove committed placeholder keys |
| `IsoAudit.Api/Program.cs:14-25` | Modified | Enable issuer/audience/HTTPS validation |
| `IsoSwitch.Infrastructure.SwitchIso8583/Net/TcpIsoClient.cs:69,112` | Modified | Replace Console.WriteLine PAN/hex with safe ILogger |
| `CardVault/IsoSwitch/IsoAudit Program.cs` (CORS) | Modified | Allowlist origins |
| `CardVault.Api/Controllers/AuthController.cs:24` | Modified | Remove `[AllowAnonymous]`, add `[Authorize]` (interim) |
| `CardVault.Api/Background/SwitchTxnConsumer.cs:344-398`, `Services/BillingService.cs` | Modified | Delegate totaling to single source |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Local dev / CI breaks when secrets removed | High | Provide user-secrets / env template; gate dev HTTPS; document setup in tasks |
| CORS allowlist blocks legitimate frontend origin | Medium | Source origins from config; verify against current frontend host before merge |
| `/register` lockdown breaks seeders/scripts relying on anonymous register | Medium | Audit seeding paths in `sdd-spec`; seeders use internal primitive, not HTTP |
| Statement-dedup refactor changes a total subtly | Medium | TDD: characterization test pins current totals before refactor |
| Scope creep into `secure-user-registration` | Medium | Explicit interim boundary; no invitation logic in this change |

## Rollback Plan
Each slice is an isolated commit/PR (auto-chain). Revert the offending slice's commit. For secret validation, a hotfix can re-supply config without reverting code. Security-tightening rollbacks (CORS, `/register`) must fix forward and open a security finding rather than re-loosening — re-adding `[AllowAnonymous]` requires an incident ticket.

## Dependencies
- Existing `CanManageUsersRoles` policy (CardVault). 
- No external/infra dependencies. mTLS track and full registration flow are explicitly separate.

## Suggested Slicing (auto-chain, ~400-line budget each)
1. **Secrets + startup validation** (all 3 services + appsettings) — highest blast radius, do first.
2. **IsoAudit JWT hardening** — small, depends on slice 1 options.
3. **PAN/ISO log leakage** (TcpIsoClient) — isolated, no config coupling.
4. **CORS allowlist** (3 services) — isolated config + Program.cs.
5. **`/register` interim lockdown** — one-attribute + authz test.
6. **Statement-recalc dedup** — correctness refactor, characterization-test gated.

## Success Criteria
- [ ] Each service fails to start (test-asserted) when its JWT/Tokenization secret is missing or equals a DEV placeholder.
- [ ] No `DEV_ONLY...` literal or placeholder key remains in code or committed `appsettings.json`.
- [ ] IsoAudit rejects tokens with wrong issuer/audience; requires HTTPS metadata outside Development.
- [ ] No PAN or raw ISO bytes appear in any log output (test-asserted).
- [ ] CORS rejects an origin not in the allowlist.
- [ ] Anonymous `POST /api/auth/register` returns 401; caller without `CanManageUsersRoles` returns 403.
- [ ] Statement totals computed in exactly one method; consumer and `GenerateStatementAsync` produce identical results (test-asserted).
- [ ] `dotnet test backend/CardSwitchPlatform.sln` stays green (596+).
