# Delta Spec — Ola 0 Security Hardening Quick Wins
# Capability: security-hardening
# Change: ola0-security-hardening
# Base spec: (no prior base spec — this change introduces new SHALL constraints across existing services)

This document records ONLY what changes. It describes the WHAT (behavioral contracts), not the HOW (implementation).
Unchanged behaviors are not repeated here.

---

## Seeder Audit Finding (REQUIRED investigation — see proposal risk)

**Finding: seeders do NOT use the HTTP /register endpoint. They are SAFE.**

`CardVault.Api/Program.cs` lines 364–410 show the startup seeder calls
`UserManager<AppUser>.CreateAsync(user, password)` and `RoleManager<IdentityRole>.CreateAsync(role)`
directly via ASP.NET Core Identity primitives, inside a `using (var scope = app.Services.CreateScope())`
block that runs before `app.Run()`. There is no HTTP call to `POST /api/auth/register` anywhere in the
seeding path.

**Consequence for spec**: locking `/register` behind `[Authorize(Policy="CanManageUsersRoles")]` does NOT
break the startup seeder. No seeder bypass mechanism or internal HTTP call is needed.

---

## ADDED Requirements

### Requirement SEC-1: Startup Secret Validation — IsoSwitch

IsoSwitch SHALL refuse to start when `Tokenization:Secret` is absent, empty, or equals the known
DEV placeholder value `"DEV_ONLY_CHANGE_ME"`.

The validation SHALL be performed via IOptions + `ValidateDataAnnotations().ValidateOnStart()` (or an
equivalent mechanism that throws on `IHost.StartAsync`). The host process SHALL exit with a non-zero
exit code when validation fails.

Minimum accepted secret length: 32 characters.

#### Scenario: Missing Tokenization:Secret causes startup failure

- GIVEN `Tokenization:Secret` is absent from all configuration sources
- WHEN `IsoSwitch.Api` host starts
- THEN the host throws `OptionsValidationException` (or equivalent) before accepting any HTTP traffic
- AND the process does not reach a healthy state

#### Scenario: DEV placeholder secret causes startup failure

- GIVEN `Tokenization:Secret` is set to the literal value `"DEV_ONLY_CHANGE_ME"`
- WHEN `IsoSwitch.Api` host starts
- THEN the host throws before accepting any HTTP traffic
- AND the error message MUST reference `Tokenization:Secret` so the operator knows which value to fix

#### Scenario: Valid 32-character secret allows startup

- GIVEN `Tokenization:Secret` is a non-placeholder string of at least 32 characters
- WHEN `IsoSwitch.Api` host starts
- THEN the host starts successfully and reaches healthy state

---

### Requirement SEC-2: Startup Secret Validation — CardVault

CardVault SHALL refuse to start when `Jwt:SigningKey` is absent, empty, or equals the known DEV
placeholder value `"DEV_ONLY_change_me_please_32+chars"`.

Minimum accepted key length: 32 characters.

#### Scenario: Missing Jwt:SigningKey causes startup failure

- GIVEN `Jwt:SigningKey` is absent from all configuration sources (or left as the C# property default)
- WHEN `CardVault.Api` host starts
- THEN the host throws before accepting any HTTP traffic

#### Scenario: DEV placeholder key causes startup failure

- GIVEN `Jwt:SigningKey` equals `"DEV_ONLY_change_me_please_32+chars"` (the compiled-in default)
- WHEN `CardVault.Api` host starts
- THEN the host throws before accepting any HTTP traffic

#### Scenario: Valid signing key allows startup

- GIVEN `Jwt:SigningKey` is a non-placeholder string of at least 32 characters
- WHEN `CardVault.Api` host starts
- THEN the host starts successfully

---

### Requirement SEC-3: Startup Secret Validation — IsoAudit

IsoAudit SHALL refuse to start when `Jwt:Key` is absent, empty, or equals the known DEV placeholder
value `"DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM"`.

Minimum accepted key length: 32 characters.

#### Scenario: Missing Jwt:Key causes startup failure

- GIVEN `Jwt:Key` is absent from all configuration sources
- WHEN `IsoAudit.Api` host starts
- THEN the host throws before accepting any HTTP traffic

#### Scenario: DEV placeholder key causes startup failure

- GIVEN `Jwt:Key` equals `"DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM"` (the compiled-in fallback in `?? "..."`)
- WHEN `IsoAudit.Api` host starts
- THEN the host throws before accepting any HTTP traffic

#### Scenario: Valid key allows startup

- GIVEN `Jwt:Key` is a non-placeholder string of at least 32 characters
- WHEN `IsoAudit.Api` host starts
- THEN the host starts successfully

---

### Requirement SEC-4: IsoAudit JWT Validation Hardening

IsoAudit SHALL validate token issuer and audience on every protected endpoint.
IsoAudit SHALL require HTTPS for metadata endpoints in all environments except `Development`.

Specifically:
- `ValidateIssuer` MUST be `true`; valid issuer MUST be sourced from configuration (e.g., `Jwt:Issuer`)
- `ValidateAudience` MUST be `true`; valid audience MUST be sourced from configuration (e.g., `Jwt:Audience`)
- `RequireHttpsMetadata` MUST be `true` unless the current environment is `Development`

#### Scenario: Token with wrong issuer is rejected

- GIVEN IsoAudit is running in any environment
- AND a valid JWT is signed with the correct key BUT carries issuer `"wrong-issuer"`
- WHEN the caller presents that token to `GET /api/audit/logs`
- THEN the system returns `401 Unauthorized`
- AND no audit log data is returned

#### Scenario: Token with wrong audience is rejected

- GIVEN IsoAudit is running in any environment
- AND a valid JWT is signed with the correct key and issuer BUT carries audience `"wrong-audience"`
- WHEN the caller presents that token to `GET /api/audit/logs`
- THEN the system returns `401 Unauthorized`

#### Scenario: Development environment does not require HTTPS metadata

- GIVEN the `ASPNETCORE_ENVIRONMENT` is set to `"Development"`
- WHEN IsoAudit starts and configures the JWT bearer handler
- THEN `RequireHttpsMetadata` is `false` for that environment only
- AND the service starts without TLS certificate configuration

#### Scenario: Non-Development environment requires HTTPS metadata

- GIVEN the `ASPNETCORE_ENVIRONMENT` is NOT `"Development"` (e.g., `"Production"`, `"Staging"`)
- WHEN IsoAudit starts
- THEN `RequireHttpsMetadata` is `true`
- AND the JwtBearer middleware enforces HTTPS for metadata document retrieval

---

### Requirement SEC-5: No Cardholder Data in Logs — ISO TCP Client

The ISO TCP client (TcpIsoClient) SHALL NOT write PAN, Track2 data, PIN block, or raw ISO 8583
payload bytes (in any encoding: binary, hex, Base64, or similar) to any log sink on any code path,
including error and debug paths.

On send/receive failure, the client SHALL log only:
- The MTI of the attempted message (if available before failure)
- A generic human-readable error message (e.g., "ISO exchange failed")
- The exception type and message (no stack trace containing payload)

The client SHALL use `ILogger<TcpIsoClient>` (structured logging) rather than `Console.WriteLine`.

#### Scenario: Send failure logs MTI and generic message only

- GIVEN the TCP connection to the acquirer host fails mid-exchange
- WHEN `TcpIsoClient.SendAsync` catches the exception
- THEN the log output contains the MTI of the attempted request AND a generic failure message
- AND the log output does NOT contain any Base64-encoded payload bytes
- AND the log output does NOT contain any hex-encoded payload bytes
- AND the log output does NOT contain any raw binary representation of the ISO message

#### Scenario: Receive failure logs MTI and generic message only

- GIVEN a valid request is sent but the response parsing fails
- WHEN `TcpIsoClient.SendFramedAsync` (or equivalent) catches the exception
- THEN the log output contains the MTI of the request AND a generic failure message
- AND the log output does NOT contain `Convert.ToHexString(respPayload)` or equivalent hex/Base64 of the response bytes

---

### Requirement SEC-6: CORS Allowlist

Each API service (CardVault, IsoSwitch, IsoAudit) SHALL restrict cross-origin requests to an
operator-configured list of origins. Wildcard `AllowAnyOrigin()` SHALL NOT be present in production-
capable configuration.

The allowed origins list SHALL be sourced from configuration (e.g., `Cors:AllowedOrigins`) and applied
via `WithOrigins(...)` at CORS policy registration time.

#### Scenario: Cross-origin request from unlisted origin is rejected

- GIVEN a CORS preflight (or simple cross-origin) request arrives with `Origin: https://evil.example.com`
- AND `https://evil.example.com` is NOT in the configured `Cors:AllowedOrigins` list
- WHEN the request is processed by the CORS middleware
- THEN the response does NOT include `Access-Control-Allow-Origin`
- AND the browser treats the request as blocked

#### Scenario: Cross-origin request from allowlisted origin is permitted

- GIVEN a CORS preflight request arrives with `Origin: https://app.example.com`
- AND `https://app.example.com` IS in the configured `Cors:AllowedOrigins` list
- WHEN the request is processed by the CORS middleware
- THEN the response includes `Access-Control-Allow-Origin: https://app.example.com`

---

### Requirement SEC-7: /register Authorization — INTERIM Contract

> **Cross-reference**: The FULL self-registration / invitation flow is owned by the
> `secure-user-registration` change (see `openspec/changes/secure-user-registration/specs/identity-and-access/spec.md`).
> This requirement covers ONLY the interim lockdown of the existing `/register` endpoint.
> Do NOT implement invitation logic here. Do NOT contradict `secure-user-registration` spec.

`POST /api/auth/register` in CardVault SHALL require an authenticated caller that satisfies the
`CanManageUsersRoles` authorization policy.

`[AllowAnonymous]` SHALL NOT be present on the `Register` action (or any encompassing controller
attribute that would bypass authentication on this endpoint) once this change is applied.

The `CanManageUsersRoles` policy (already defined in `Program.cs`) grants access to callers with the
`Admin` role or the `users:manage` permission claim. No new policy is introduced by this change.

#### Scenario: Anonymous POST /api/auth/register returns 401

- GIVEN an unauthenticated caller (no bearer token in the `Authorization` header)
- WHEN the caller sends `POST /api/auth/register` with any valid or invalid payload
- THEN the system returns `401 Unauthorized`
- AND no user account is created

#### Scenario: Authenticated caller without CanManageUsersRoles returns 403

- GIVEN a caller authenticated with a valid JWT that does NOT satisfy `CanManageUsersRoles`
  (e.g., role `"Auditor"` with no `users:manage` claim)
- WHEN the caller sends `POST /api/auth/register`
- THEN the system returns `403 Forbidden`
- AND no user account is created

#### Scenario: Caller with CanManageUsersRoles can proceed

- GIVEN a caller authenticated with a valid JWT that satisfies `CanManageUsersRoles`
  (e.g., role `"Admin"`)
- WHEN the caller sends `POST /api/auth/register` with a valid registration payload
- THEN the system processes the registration request normally (returns 200/201 on success or
  the appropriate validation error on bad payload)
- AND the `CanManageUsersRoles` gate does NOT add a new error condition for otherwise valid requests

---

### Requirement SEC-8: Single Source of Statement Totals

Statement totaling — specifically the computation of `TotalPaymentDue` and `NewBalance` — SHALL be
performed in exactly one method shared by both the switch transaction consumer path
(`SwitchTxnConsumer.UpdateOpenStatementAsync`) and the billing generation path
(`BillingService.GenerateStatementAsync`).

For any given set of ledger entries and a statement period, both paths SHALL produce identical
`TotalPaymentDue` and `NewBalance` values.

The following formula, currently duplicated across both paths, defines the contractual totaling behavior:

```
NewBalance           = PreviousBalance + Purchases + Payments + Fees + Interest
PrincipalDue         = Max(0, NewBalance − InterestDue − FeesDue)
TotalPaymentDue      = PrincipalDue + InterestDue + FeesDue
NewBalance (final)   = TotalPaymentDue
```

This formula SHALL be the single authoritative computation. No other code path SHALL independently
recompute these two fields using a different formula.

#### Scenario: Characterization — consumer path and billing path produce identical totals

- GIVEN a set of ledger entries for a known account and billing cycle:
  - PreviousBalance = 100.00
  - Purchases = 200.00
  - Payments = −50.00
  - Fees = 10.00
  - Interest = 5.00
- WHEN the totaling logic is invoked via the switch consumer path for an open statement
- AND the same logic is invoked via `BillingService.GenerateStatementAsync` for the same inputs
- THEN both paths yield:
  - `TotalPaymentDue` = 265.00
  - `NewBalance` = 265.00
- AND neither path deviates from the contractual formula above

> **Implementation note for tasks/apply**: a characterization test MUST be written BEFORE refactoring
> the formula into a shared method, to pin existing behavior. The refactor is gated on that test passing.

---

### Requirement SEC-9: No Secret Material in Committed Configuration — Env-Only Secret Loading

CardVault and IsoSwitch SHALL load all secret material — vault encryption keys, database connection-string
passwords, JWT signing keys, tokenization secrets, seed administrative credentials, and admin API keys —
exclusively from environment variables or a secrets-manager configuration provider. No committed file in the
repository SHALL contain live secret material.

Specifically:
- `appsettings.Development.json` (CardVault and IsoSwitch) SHALL NOT contain any live vault key
  (`Vault:Keys:*`), any connection string with an inline password, any seed credential, or any admin API key.
- Secret-bearing options types SHALL follow the established repo convention: the secret property is
  intentionally absent from the options type and read from configuration/environment directly (as with
  `SendGridOptions` / `MovistarOptions`, "`ApiKey` is a secret and is intentionally NOT a property here").
- A committed non-secret template (`.env.example` or an appsettings skeleton with empty/placeholder values)
  SHALL document every variable an operator must supply. Placeholder values SHALL be obviously non-secret.

> **One-way-door note (git history rewrite):** The proposal also requires purging leaked values from git
> history and rotating the compromised keys `k1`/`k2`. Git-history rewrite is an operational, irreversible
> action; the *behavioral* contract this spec pins is "no live secret in any committed file and env-only
> loading at runtime". The history-scrub and key-rotation sequencing (rotate → re-encrypt → revoke) is
> owned by SEC-01/vault-and-pci and the design phase, not silently decided here.

#### Scenario: Committed development config contains no live vault key

- GIVEN the repository at the tip of the phase0 branch
- WHEN `appsettings.Development.json` for CardVault is inspected
- THEN it contains no value matching a Base64 AES-256 key under `Vault:Keys`
- AND the previously committed values `k1` and `k2` are absent

#### Scenario: Committed config contains no inline connection-string password

- GIVEN the repository at the tip of the phase0 branch
- WHEN any committed `appsettings*.json` is inspected
- THEN no connection string contains an inline `Password=` value
- AND the connection strings are sourced at runtime from environment variables (e.g. `ConnectionStrings__Postgres`)

#### Scenario: Missing required secret env var causes fail-fast startup

- GIVEN a required secret variable (e.g. `Vault__Keys__<activeKeyId>` or `ConnectionStrings__Postgres`) is
  absent from all configuration sources
- WHEN `CardVault.Api` host starts
- THEN the host throws before accepting any HTTP traffic
- AND the process exits with a non-zero exit code
- AND the error message references the missing configuration key so the operator knows what to supply

#### Scenario: A committed .env.example documents required variables with non-secret placeholders

- GIVEN the committed `.env.example` template
- WHEN it is inspected
- THEN it lists every operator-supplied secret variable name
- AND every value is an obvious placeholder (empty or a clearly non-secret token), never live secret material

---

### Requirement SEC-10: ISO 8583 TCP Channel — TLS Default-On and Production Fail-Fast for Non-Loopback Hosts

The ISO 8583 TCP client (`TcpIsoClientOptions`) SHALL default `UseTls` to `true`.

IsoSwitch SHALL fail startup in the `Production` environment when TLS is disabled (`UseTls = false`) for a
non-loopback acquirer host. Plaintext SHALL remain permitted only when the configured acquirer host is a
loopback address (`localhost`, `127.0.0.1`, or `::1`), and outside `Production`. The existing constraint that
`AllowInvalidCert` is gated to `Development` SHALL remain unchanged.

#### Scenario: UseTls defaults to true when unspecified

- GIVEN no explicit `UseTls` value is provided in configuration
- WHEN `TcpIsoClientOptions` is bound
- THEN `UseTls` resolves to `true`

#### Scenario: Production with TLS disabled for a non-loopback host fails startup

- GIVEN `ASPNETCORE_ENVIRONMENT` is `Production`
- AND the configured acquirer host is a non-loopback address (e.g. `acquirer.example.com`)
- AND `UseTls` is set to `false`
- WHEN `IsoSwitch.Api` host starts
- THEN the host throws before accepting any HTTP traffic
- AND the error message references the ISO TCP TLS setting and the offending host
- AND the process exits with a non-zero exit code

#### Scenario: Production with TLS disabled for a loopback host is permitted

- GIVEN `ASPNETCORE_ENVIRONMENT` is `Production`
- AND the configured acquirer host is `127.0.0.1` (loopback simulator)
- AND `UseTls` is set to `false`
- WHEN `IsoSwitch.Api` host starts
- THEN startup succeeds
- AND the plaintext loopback path remains available for the simulator

#### Scenario: Development with TLS disabled for a non-loopback host is permitted

- GIVEN `ASPNETCORE_ENVIRONMENT` is `Development`
- AND `UseTls` is set to `false` for a non-loopback host
- WHEN `IsoSwitch.Api` host starts
- THEN startup succeeds (the fail-fast applies only in `Production`)

---

### Requirement SEC-11: IsoSwitch Admin API Key — Operator-Supplied, Fail-Fast, Rejects DEV Placeholder

IsoSwitch SHALL require the admin API key to be supplied via configuration (environment / secret-manager
provider) and SHALL refuse to start when the admin API key is absent, empty, or equals the known DEV
placeholder value `"dev-admin-key"`. This mirrors the existing SEC-1/SEC-2/SEC-3 fail-fast startup-validation
pattern. The `dev-admin-key` literal SHALL NOT appear in any committed configuration file after this change.

#### Scenario: Missing admin API key causes startup failure

- GIVEN the IsoSwitch admin API key is absent from all configuration sources
- WHEN `IsoSwitch.Api` host starts
- THEN the host throws before accepting any HTTP traffic
- AND the process exits with a non-zero exit code

#### Scenario: DEV placeholder admin API key causes startup failure

- GIVEN the IsoSwitch admin API key is set to the literal value `"dev-admin-key"`
- WHEN `IsoSwitch.Api` host starts
- THEN the host throws before accepting any HTTP traffic
- AND the error message references the admin API key configuration so the operator knows which value to fix

#### Scenario: Valid operator-supplied admin API key allows startup

- GIVEN the admin API key is a non-placeholder operator-supplied value
- WHEN `IsoSwitch.Api` host starts
- THEN the host starts successfully and reaches healthy state

#### Scenario: Committed config does not contain the dev-admin-key placeholder

- GIVEN the repository at the tip of the phase0 branch
- WHEN `appsettings.Development.json` for IsoSwitch is inspected
- THEN it does not contain the literal `"dev-admin-key"`

---

### Requirement SEC-12: CardVault Response Security Headers

CardVault SHALL emit the following response security headers on API responses:
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Content-Security-Policy` including a `frame-ancestors` directive consistent with the `X-Frame-Options: DENY`
  intent (i.e. `frame-ancestors 'none'`).

The exact remaining CSP directive set (script/style/connect sources, and any Development-only relaxations
needed for Swagger UI) is an implementation concern for the design/apply phase; this requirement pins that the
three headers are present and that framing is denied.

#### Scenario: Responses carry X-Content-Type-Options nosniff

- GIVEN CardVault is running
- WHEN any client issues a request to a CardVault endpoint and receives a response
- THEN the response includes `X-Content-Type-Options: nosniff`

#### Scenario: Responses deny framing

- GIVEN CardVault is running
- WHEN a client receives a CardVault response
- THEN the response includes `X-Frame-Options: DENY`
- AND the response includes a `Content-Security-Policy` header whose `frame-ancestors` directive is `'none'`

#### Scenario: Content-Security-Policy header is present

- GIVEN CardVault is running
- WHEN a client receives a CardVault response
- THEN the response includes a non-empty `Content-Security-Policy` header

---

## Out-of-Scope Confirmations

The following are explicitly NOT specified in this delta:

- mTLS / client-certificate authentication and IP allowlisting on the ISO 8583 TCP channel — Phase 1.
- Acquirer / internal-CA certificate provisioning and rotation infrastructure — Phase 1.
- The definitive HSM-backed PIN verification control (interim KDF is specified under vault-and-pci) — Phase 1.
- Redesign of the CORS policy beyond what SEC-03 cookie delivery requires — the credentials/SameSite decision
  for the `localhost:4200` dev origin is pinned behaviorally in identity-and-access and finalized at design.
