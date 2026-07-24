# Delta Spec — Phase 0 Security Blockers
# Capability: security-hardening
# Change: phase0-security-blockers
# Base spec: openspec/specs/security-hardening/spec.md (ola0-security-hardening SEC-1..SEC-8)

This document records ONLY what changes. It describes the WHAT (behavioral contracts), not the HOW (implementation).
Unchanged behaviors from the base spec are not repeated here. SEC-9..SEC-12 below are ADDED requirements
that extend the existing fail-fast / no-secret-leak family. Where a Phase 0 requirement supersedes a prior
out-of-scope confirmation from the base spec, that supersession is stated explicitly.

---

## Supersession Notes

- The base spec's out-of-scope confirmation "TCP ISO 8583 mTLS / IP allowlist (port 7000) — separate track"
  is PARTIALLY superseded: **server-side TLS on the ISO TCP channel is now IN SCOPE** (SEC-10 below).
  mTLS, client-certificate authentication, and IP allowlisting remain deferred to Phase 1.
- The base spec's out-of-scope confirmation "Plaintext `Password=postgres` in connection strings — flagged P2"
  is superseded by SEC-9: connection-string passwords are now purged from committed config and sourced from
  environment/secret-manager providers.

---

## ADDED Requirements

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

## Out-of-Scope Confirmations (Phase 0)

- mTLS / client-certificate authentication and IP allowlisting on the ISO 8583 TCP channel — Phase 1.
- Acquirer / internal-CA certificate provisioning and rotation infrastructure — Phase 1.
- The definitive HSM-backed PIN verification control (interim KDF is specified under vault-and-pci) — Phase 1.
- Redesign of the CORS policy beyond what SEC-03 cookie delivery requires — the credentials/SameSite decision
  for the `localhost:4200` dev origin is pinned behaviorally in identity-and-access and finalized at design.
