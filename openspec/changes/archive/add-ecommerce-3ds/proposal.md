# Proposal: Add Ecommerce 3DS Challenge Flow

## Why

Sprint 8 still lists `v70` as pending, and the current backend does not provide a dedicated 3D Secure flow for ecommerce purchases. The platform already supports issuer, antifraud, vault, and audit capabilities, so the missing gap is an ecommerce-specific authentication challenge that can:

- start a 3DS challenge for an online card payment
- validate a one-time password with bounded retries
- produce an allow or reject decision for the purchase
- expose monitoring data for operations and audit users

## Scope

Add a CardVault-managed ecommerce 3DS flow with:

- challenge initiation endpoint
- OTP verification endpoint
- challenge detail and monitoring endpoints
- persisted challenge state, expiry, and attempt tracking
- simple ecommerce risk scoring based on request context and recent failures
- PCI-safe audit publication for challenge lifecycle events

## Out Of Scope

- external network integrations with Visa or Mastercard directory servers
- real SMS or push delivery providers
- browser SDK, ACS UI rendering, or frontend implementation
- OAuth/Open Banking features from later roadmap items

## Impacted Areas

- `backend/services/CardVault/src/CardVault.Api`
- `backend/services/CardVault/src/CardVault.Infrastructure.Persistence`
- `openspec/specs/`
- `funcional/Sprints.md`
