# Delta Spec — Phase 0 Security Blockers
# Capability: identity-and-access
# Change: phase0-security-blockers
# Base spec: openspec/specs/identity-and-access/spec.md

This document records ONLY what changes. It describes the WHAT (behavioral contracts), not the HOW.
It ADDS a cookie-based token-delivery requirement (SEC-03) and MODIFIES the base spec's
"Seeded development users are available" scenario so administrative seeding occurs only in `Development`
(SEC-05). Unchanged authentication and authorization behavior from the base spec is not repeated.

---

## MODIFIED Requirements

### Requirement: JWT-Based Authentication (modified scenario — Development-only admin seeding)

The base requirement "The system SHALL authenticate valid users through CardVault and issue bearer-token
based access for protected APIs." is unchanged. The base scenario "Seeded development users are available"
is REPLACED by the two scenarios below so that administrative seeding is confined to `Development` and no
known-credential admin is ever auto-provisioned in a non-Development environment.

CardVault SHALL seed the default administrative user only when the environment is `Development`. CardVault
SHALL NOT carry compiled-in credential fallbacks (e.g. `?? "admin@demo.com"` / `?? "Admin1234!"`); absent
configuration SHALL NOT silently produce a known admin. In non-Development environments, administrative
provisioning is an explicit, controlled operation with no auto-seed.

#### Scenario: Development seeds default operator roles and admin user

- GIVEN `ASPNETCORE_ENVIRONMENT` is `Development`
- AND the identity store is empty
- WHEN CardVault starts
- THEN CardVault seeds the default operator roles and the default administrative user needed for local testing

#### Scenario: Non-Development never auto-seeds a default admin

- GIVEN `ASPNETCORE_ENVIRONMENT` is NOT `Development` (e.g. `Production`, `Staging`)
- AND the identity store is empty
- WHEN CardVault starts
- THEN no administrative user is auto-seeded
- AND no `admin@demo.com` / `Admin1234!` credential is created via any compiled-in fallback
- AND startup does not depend on a hardcoded credential default

---

## ADDED Requirements

### Requirement: Cookie-Based Token Delivery (SEC-03)

CardVault SHALL deliver access and refresh tokens to browser clients as `HttpOnly; Secure; SameSite` cookies
and SHALL accept the access token from the cookie in the authentication pipeline, so that token material is
not readable by client-side JavaScript. Token refresh and logout SHALL operate against the cookie model:
refresh reads the refresh-token cookie and reissues cookies; logout clears the token cookies.

The `SameSite` attribute value and whether CORS is configured with `AllowCredentials` for the
`http://localhost:4200` development origin is a **one-way-door decision deferred to design** (see proposal
risk "SameSite/Secure cookie choice conflicts with cross-origin dev setup"). This requirement pins the
observable behavior below and requires the chosen value to keep the Development SPA flow working while never
relaxing `Secure`/`HttpOnly` in `Production`; the specific `SameSite` value (`Strict` vs `Lax` vs `None`) is
selected at design, not invented here.

#### Scenario: Successful login issues HttpOnly Secure token cookies

- GIVEN an enabled user submits valid credentials to the CardVault authentication endpoint
- WHEN authentication succeeds
- THEN the response sets an access-token cookie and a refresh-token cookie
- AND each token cookie carries the `HttpOnly` attribute
- AND each token cookie carries the `Secure` attribute
- AND each token cookie carries a `SameSite` attribute
- AND the token material is NOT returned in a form readable by client-side JavaScript (not in a JS-readable body field relied upon for storage)

#### Scenario: Protected endpoint accepts the token from the cookie

- GIVEN a client holds a valid access-token cookie issued by CardVault
- AND the client sends no `Authorization` header
- WHEN the client requests a protected CardVault endpoint with the cookie
- THEN the request is authenticated and authorized on the basis of the cookie token
- AND the endpoint responds as it would for an equivalently authorized bearer-token caller

#### Scenario: Refresh reissues cookies from the refresh cookie

- GIVEN a client holds a valid refresh-token cookie
- WHEN the client calls the refresh endpoint with the cookie
- THEN CardVault validates the refresh token from the cookie
- AND sets a new access-token cookie (and rotated refresh-token cookie per the existing refresh policy)
- AND does not require the refresh token to be supplied in the request body

#### Scenario: Logout clears the token cookies

- GIVEN an authenticated client with access- and refresh-token cookies
- WHEN the client calls the logout endpoint
- THEN CardVault clears (expires) both token cookies
- AND a subsequent request to a protected endpoint using the cleared cookies is rejected with `401 Unauthorized`

#### Scenario: Production never relaxes HttpOnly or Secure

- GIVEN `ASPNETCORE_ENVIRONMENT` is `Production`
- WHEN CardVault issues token cookies
- THEN every token cookie carries `HttpOnly` and `Secure`
- AND no Development-only relaxation of these attributes applies

---

## Out-of-Scope Confirmations (Phase 0)

- Full self-registration / invitation flow — owned by `secure-user-registration`.
- Password recovery flow (IAM-PR-*) — not re-opened by this change.
- MFA end-to-end wiring — deferred.
- Client-side token-expiry parsing rework (`isTokenExpired`) is a frontend implementation detail of SEC-03,
  captured under the design/apply phase, not a separate spec requirement here.
