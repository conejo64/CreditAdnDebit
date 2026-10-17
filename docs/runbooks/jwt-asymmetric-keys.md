# JWT signing keys (ES256) — generation, distribution, rotation

Since Gate 1 (G1-T5) every JWT in the platform is signed by **CardVault** with an **ES256**
(ECDSA, curve P-256) private key, and **IsoSwitch**, **IsoAudit** and CardVault itself validate
with the matching **public key**. No service holds a shared secret any more: a validator cannot
mint a token, and a leaked public key is harmless. The retired HS256 settings
(`Jwt:SigningKey`, `Jwt:Key`) are rejected at startup.

Tokens carry a `kid` header — the RFC 7638 thumbprint of the public key — so a rotation is
visible as a key-id change and nothing else. CardVault publishes the current public key at
`GET /.well-known/jwks.json` (anonymous, `Cache-Control: public, max-age=300`).

## Who gets which key

| Service   | Role      | Settings                                                                 |
|-----------|-----------|--------------------------------------------------------------------------|
| CardVault | issuer    | `Jwt:PrivateKeyPem` **or** `Jwt:PrivateKeyPath` (required); `Jwt:PublicKeyPem` / `Jwt:PublicKeyPath` optional, must pair with the private key |
| IsoSwitch | validator | `Jwt:PublicKeyPem` **or** `Jwt:PublicKeyPath` (required)                 |
| IsoAudit  | validator | `Jwt:PublicKeyPem` **or** `Jwt:PublicKeyPath` (required)                 |

All three share `Jwt:Issuer` (`CardVault`) and `Jwt:Audience` (`CardSwitch`). The MFA pending
token keeps its own audience, `<Audience>:mfa-pending`, and is signed with the same key.

Environment-variable form: `Jwt__PrivateKeyPem`, `Jwt__PrivateKeyPath`, `Jwt__PublicKeyPem`,
`Jwt__PublicKeyPath`. A PEM is multi-line; where a single line is required (`.env` files, most
CI secret stores) write it with `\n` escape sequences — the services accept both forms.

## Generate a key pair

```bash
# Private key, PKCS#8 PEM (PEM header type `PRIVATE KEY`)
openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt -out cardvault-jwt-private.pem

# Public key, SubjectPublicKeyInfo PEM (PEM header type `PUBLIC KEY`)
openssl ec -in cardvault-jwt-private.pem -pubout -out cardvault-jwt-public.pem

# Optional: the kid the services will derive (RFC 7638 thumbprint) — useful to confirm
# every validator picked up the same key. Requires python3 + cryptography.
python3 - <<'EOF'
import base64, hashlib, json
from cryptography.hazmat.primitives import serialization
pub = serialization.load_pem_public_key(open("cardvault-jwt-public.pem","rb").read()).public_numbers()
b64 = lambda n: base64.urlsafe_b64encode(n.to_bytes(32, "big")).rstrip(b"=").decode()
canonical = json.dumps({"crv":"P-256","kty":"EC","x":b64(pub.x),"y":b64(pub.y)}, separators=(",",":"))
print(base64.urlsafe_b64encode(hashlib.sha256(canonical.encode()).digest()).rstrip(b"=").decode())
EOF
```

Only P-256 is accepted; a P-384/P-521 or RSA key fails startup.

### Single-line PEM for `.env`

```bash
awk 'NF {printf "%s\\n", $0}' cardvault-jwt-public.pem
```

## docker-compose (`backend/deploy`)

`docker-compose.yml` expects:

- `JWT_PUBLIC_KEY_PEM` in `.env` — the public key (single line, `\n` escapes). Compose maps
  it to `Jwt__PublicKeyPem` for all three services.
- `./secrets/cardvault-jwt-private.pem` — the private key file, mounted as a compose secret
  into **cardvault only** and read through `Jwt__PrivateKeyPath=/run/secrets/cardvault_jwt_private_key`.
  `secrets/` and `*.pem` are gitignored.

The private key is deliberately not a `.env` variable: `env_file: .env` injects every variable
into all three containers, which would hand the signing key to the validators.

Remove `Jwt__SigningKey` and `Jwt__Key` from `.env`: any service that still sees one refuses
to start. (`backend/deploy/.env.example` still lists the retired variables — see "Known gaps".)

## Local development

- Running CardVault alone: set `Jwt:DevelopmentEphemeralKeys=true` **and** run with
  `ASPNETCORE_ENVIRONMENT=Development`. CardVault generates a throwaway pair for the process
  and logs a warning with the kid. Tokens die with the process and no other service can
  validate them. The flag is refused outside Development and when a key is also configured.
- Running several services: generate a pair as above and put the public key in every
  service's user secrets (`dotnet user-secrets set "Jwt:PublicKeyPem" "$(cat cardvault-jwt-public.pem)"`)
  and the private key in CardVault's (`"Jwt:PrivateKeyPem"`).
- Tests never read key material from configuration files: each test factory generates an
  ephemeral pair per test process and signs with its private half.

## Rotation

Goal: no token issued before the rotation is rejected while it is still valid
(`Jwt:AccessTokenMinutes`, default 15), and no window in which a validator lacks the key
CardVault signs with.

1. Generate the new pair (`kid_new`). Keep the old one (`kid_old`) at hand.
2. **Validators first.** Deploy IsoSwitch and IsoAudit with `Jwt:PublicKeyPem = kid_new`.
   The current services hold exactly one public key, so from this moment they reject
   `kid_old` tokens — schedule the next step immediately, or accept a re-login for active
   sessions (the frontend refresh flow obtains a `kid_new` token transparently as long as the
   refresh token is still valid).
3. **Issuer.** Deploy CardVault with the new private key (and, if configured, the new public
   key). Confirm the startup log line `JWT signing key loaded: alg=ES256 kid=<kid_new>` and
   that `/.well-known/jwks.json` returns `kid_new`.
4. Destroy the old private key. Refresh tokens are unaffected (they are opaque, hashed at
   rest, and not JWTs).

Overlap window: the services accept one public key each. A true overlap (validators trusting
`kid_old` and `kid_new` simultaneously) requires a multi-key `TokenValidationParameters.IssuerSigningKeys`
or fetching the JWKS at startup; neither is implemented in this slice (see "Known gaps").
Until then, rotate inside a maintenance window or accept one forced refresh.

## Startup failure messages

Every service validates its `Jwt` section on start (`ValidateOnStart`). The host does not
listen when validation fails; the message names the setting to fix.

| Situation | Service | Message (prefix) |
|---|---|---|
| Retired HS256 setting present | all | `Jwt:SigningKey is no longer supported: JWTs are signed with an ES256 key pair.` / `Jwt:Key is no longer supported: …` |
| `Jwt:Issuer` / `Jwt:Audience` empty | all | `Jwt:Issuer is required.` / `Jwt:Audience is required.` |
| Public key missing | IsoSwitch, IsoAudit | `Jwt:PublicKeyPem (or Jwt:PublicKeyPath) is required: set the CardVault ES256 public key …` |
| Private key handed to a validator (`Jwt:PrivateKeyPem` / `Jwt:PrivateKeyPath` set) | IsoSwitch, IsoAudit | `IsoSwitch only validates tokens and must never hold the signing key: remove Jwt:PrivateKeyPem.` (same for IsoAudit) |
| Private key PEM in `Jwt:PublicKeyPem` | all | `Jwt:PublicKeyPem is invalid: the PEM contains a private key; only the public key ('-----BEGIN PUBLIC KEY-----') is accepted here.` |
| Malformed PEM | all | `Jwt:PublicKeyPem is invalid: the PEM is not a valid EC public key (…)` / `Jwt:PrivateKeyPem is invalid: …` |
| Wrong curve | all | `… the key uses a 384-bit curve; ES256 requires P-256.` |
| Key file unreadable | all | `Jwt:PublicKeyPath points to '<path>', which could not be read (…)` (same for `Jwt:PrivateKeyPath`) |
| Private key missing | CardVault | `Jwt:PrivateKeyPem (or Jwt:PrivateKeyPath) is required: CardVault signs tokens with an ES256 private key …` |
| Public key in `Jwt:PrivateKeyPem` | CardVault | `Jwt:PrivateKeyPem is invalid: the PEM does not contain a private key …` |
| Configured public key does not pair with the private key | CardVault | `Jwt:PublicKeyPem does not match Jwt:PrivateKeyPem: the public key must be derived from the configured private key.` |
| `Jwt:AccessTokenMinutes` ≤ 0 | CardVault | `Jwt:AccessTokenMinutes must be greater than zero.` |
| `Jwt:DevelopmentEphemeralKeys=true` outside Development | CardVault | `Jwt:DevelopmentEphemeralKeys is only honoured in the Development environment; configure Jwt:PrivateKeyPem instead.` |
| `Jwt:DevelopmentEphemeralKeys=true` and a key configured | CardVault | `Jwt:DevelopmentEphemeralKeys cannot be combined with Jwt:PrivateKeyPem or Jwt:PublicKeyPem: …` |

Successful start (CardVault): `JWT signing key loaded: alg=ES256 kid=<kid> ephemeral=<bool>`.
With ephemeral keys a warning precedes it:
`Jwt:DevelopmentEphemeralKeys is enabled: CardVault generated a throwaway ES256 key pair for this process. …`

## Known gaps (follow-ups, not in G1-T5)

- `backend/deploy/.env.example`, `docs/env.example`, `docs/secrets.md`, `backend/README.md`
  and `backend/SECRETS-ROTATION-RUNBOOK.md` still document `Jwt__SigningKey` / `Jwt__Key`;
  they need the variables described above.
- Validators trust a single public key. A zero-downtime rotation needs either a second
  configured public key or JWKS fetching on the validator side.
