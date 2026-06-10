# Secret Provisioning Guide

## Overview

All three services (CardVault, IsoSwitch, IsoAudit) perform fail-fast startup validation on their required secrets. If a secret is absent, empty, less than 32 characters, or matches a known DEV placeholder, the host will throw `OptionsValidationException` and refuse to start.

## Required Secrets

| Service | Config Key | Env Var | Min Length | Notes |
|---------|-----------|---------|------------|-------|
| CardVault.Api | `Jwt:SigningKey` | `Jwt__SigningKey` | 32 | Signing key for access tokens issued by CardVault |
| IsoSwitch.Api | `Tokenization:Secret` | `Tokenization__Secret` | 32 | HMAC-SHA256 key for PAN tokenization |
| IsoAudit.Api | `Jwt:Key` | `Jwt__Key` | 32 | Must match CardVault `Jwt:SigningKey` so IsoAudit can validate tokens |

## Local Development (user-secrets)

```bash
# CardVault.Api
cd backend/services/CardVault/src/CardVault.Api
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 32)"

# IsoSwitch.Api
cd backend/services/IsoSwitch/src/IsoSwitch.Api
dotnet user-secrets set "Tokenization:Secret" "$(openssl rand -base64 32)"

# IsoAudit.Api — use THE SAME VALUE as CardVault Jwt:SigningKey
cd backend/services/IsoAudit/src/IsoAudit.Api
dotnet user-secrets set "Jwt:Key" "<same-value-as-CardVault-Jwt:SigningKey>"
```

On Windows PowerShell, generate a secret with:
```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
```

## CI/CD (Environment Variables)

Set the following environment variables in your CI pipeline before running `dotnet test` or starting the services:

```
Jwt__SigningKey=<your-secret>
Tokenization__Secret=<your-secret>
Jwt__Key=<same-as-Jwt__SigningKey>
```

Double-underscore (`__`) is the ASP.NET Core convention for nested config keys in environment variables.

## Test Environments

Integration tests supply their own valid in-memory secrets via `WebApplicationFactory` configuration overrides. No real secrets are needed to run `dotnet test`.

## Forbidden Values

The following are rejected at startup regardless of length:
- Any value containing `DEV_ONLY` (case-insensitive)
- Any value containing `CHANGE_ME` (case-insensitive)
- Any value containing `change_me` (case-insensitive)
- Any value containing `placeholder` (case-insensitive)
- Any value shorter than 32 characters
- Empty or whitespace-only values
