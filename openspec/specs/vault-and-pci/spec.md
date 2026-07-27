# Vault And Pci Specification

## Purpose

Define the security and operational rules for PAN tokenization, vault key management, and PCI-safe auditing in CardVault.

## Requirements

### Requirement: Tokenized Card Data Handling
The system SHALL keep raw PAN handling inside CardVault and return masked or tokenized representations to clients and downstream systems.

#### Scenario: Card issuance or vault workflows expose safe values
- WHEN CardVault processes card-sensitive data for issuance or vault operations
- THEN external responses expose tokenized or masked values instead of raw PAN

### Requirement: Controlled Detokenization
The system MUST protect detokenization with authorization and rate limiting.

#### Scenario: Authorized detokenization succeeds within policy
- WHEN an authorized caller with the required permission performs a detokenization request within the allowed rate window
- THEN the request is processed and audited

#### Scenario: Excessive detokenization attempts are throttled
- WHEN detokenization requests exceed the configured per-minute threshold
- THEN CardVault enforces the configured rate limiter

### Requirement: Vault Key Rotation
The system SHALL support active vault key rotation and background re-encryption of stored vault entries.
The rotation and re-encryption endpoints SHALL be rate-limited via the registered `vault_admin_ops` policy.
The system SHALL return `429 Too Many Requests` when invocations exceed the configured window under that policy.
The system SHALL return `403 Forbidden` for any caller that does not satisfy the `CanRotateVaultKeys` authorization policy.

#### Scenario: Active key changes persist across restarts
- WHEN an administrator rotates the active vault key
- THEN CardVault persists the active key identifier and restores it on startup

#### Scenario: Background re-encryption progresses in batches
- WHEN stored vault records need to be re-encrypted after a key rotation
- THEN CardVault processes the re-encryption job incrementally without requiring a full-stop migration window

#### Scenario: Authorized admin rotates key within rate window
- GIVEN a caller authenticated with the `CanRotateVaultKeys` policy
- AND the number of requests in the current window has not reached the `vault_admin_ops` permit limit
- WHEN the caller invokes `POST /api/vault/rotate-active-key`
- THEN CardVault returns `200 OK` and performs the rotation

#### Scenario: Rotation request is throttled on burst
- GIVEN a caller authenticated with the `CanRotateVaultKeys` policy
- AND the number of requests in the current window equals or exceeds the `vault_admin_ops` permit limit
- WHEN the caller invokes `POST /api/vault/rotate-active-key`
- THEN CardVault returns `429 Too Many Requests`
- AND the response does not advance the cryptoperiod counter

#### Scenario: Re-encryption request is throttled on burst
- GIVEN a caller authenticated with the `CanRotateVaultKeys` policy
- AND the number of requests in the current window equals or exceeds the `vault_admin_ops` permit limit
- WHEN the caller invokes `POST /api/vault/reencrypt`
- THEN CardVault returns `429 Too Many Requests`

#### Scenario: Unauthorized caller is rejected for rotation
- GIVEN a caller that does NOT satisfy the `CanRotateVaultKeys` authorization policy
- WHEN the caller invokes `POST /api/vault/rotate-active-key` or `POST /api/vault/reencrypt`
- THEN CardVault returns `403 Forbidden`
- AND no rotation or re-encryption is performed

#### Scenario: Startup asserts vault_admin_ops policy is registered
- GIVEN the CardVault application host is starting
- WHEN the rate-limiter middleware is initialized
- THEN both `vault_detokenize` and `vault_admin_ops` policies MUST be present in the rate-limiter registry
- AND if either policy is absent, application startup fails with a descriptive error before any request is served

### Requirement: PCI-Safe Audit Events
The system MUST publish PCI-safe audit information for sensitive vault operations.
Every successful key rotation SHALL emit a `VaultKeyRotated` event containing `actor`, `keyId` (identifier only — never key material or PAN), `traceId`, and UTC timestamp.
Every completed re-encryption batch SHALL emit a `VaultReencryptionBatchCompleted` event containing `actor`, `traceId`, `recordsAffected`, and UTC timestamp.
Audit events SHALL be published through the EF outbox so that audit persistence is transactional with the rotation state change.
Event payloads SHALL never include raw key material, plaintext PAN, or any card-sensitive value.

#### Scenario: Tokenization or detokenization emits an audit event
- WHEN CardVault performs a sensitive vault operation
- THEN it emits an audit record without including raw PAN in the payload

#### Scenario: Successful key rotation emits a VaultKeyRotated audit event
- GIVEN a caller authenticated with the `CanRotateVaultKeys` policy
- WHEN `POST /api/vault/rotate-active-key` completes successfully
- THEN CardVault publishes exactly one `VaultKeyRotated` event via the outbox
- AND the event payload includes `actor` (authenticated user identity), `keyId` (opaque key identifier), `traceId` (request correlation ID), and `rotatedAt` (UTC timestamp)
- AND the event payload does NOT include any key material, plaintext PAN, or card-sensitive data

#### Scenario: Re-encryption batch emits a VaultReencryptionBatchCompleted audit event
- GIVEN a caller authenticated with the `CanRotateVaultKeys` policy
- WHEN `POST /api/vault/reencrypt` processes a batch of vault records
- THEN CardVault publishes exactly one `VaultReencryptionBatchCompleted` event per batch via the outbox
- AND the event payload includes `actor`, `traceId`, `recordsAffected` (integer count), and `completedAt` (UTC timestamp)
- AND the event payload does NOT include any key material, PAN, or card-sensitive data

#### Scenario: Audit event is not emitted on failed or throttled rotation
- GIVEN a rotation or re-encryption request that is rejected (403, 429, or any server error)
- WHEN the request is rejected before the rotation state changes
- THEN CardVault does NOT publish any `VaultKeyRotated` or `VaultReencryptionBatchCompleted` event

#### Scenario: Audit event survives transient event-bus outage via outbox
- GIVEN the event bus (Kafka) is temporarily unavailable at the moment of rotation
- WHEN CardVault successfully persists the rotation state change within the same database transaction as the outbox audit row
- THEN the `EfOutboxPublisher` relay eventually delivers the `VaultKeyRotated` event once connectivity is restored
- AND no audit event is silently lost

### Requirement: Re-Encryption Under Rotated Key With Old-Key Revocation

When the committed vault keys are rotated as part of the secret purge, CardVault SHALL re-encrypt all stored
tokenized PANs under the new active vault key using the existing batch key-rotation / re-encryption workflow,
and SHALL revoke the old key ids (`k1`, `k2`) only after re-encryption of all affected records is complete and
verified, so that no tokenized record is orphaned and the old keys can no longer decrypt any stored value.

The re-encryption SHALL reuse the existing audit contract: it emits `VaultKeyRotated` on rotation and one
`VaultReencryptionBatchCompleted` per batch via the EF outbox, with payloads that never contain key material,
plaintext PAN, or card-sensitive values (per the base "PCI-Safe Audit Events" requirement).

#### Scenario: Rotation and full re-encryption precede revocation

- GIVEN stored tokenized PANs are encrypted under old key ids `k1`/`k2`
- WHEN an administrator rotates to a new active vault key and runs the re-encryption workflow
- THEN every affected stored record is re-encrypted under the new active key
- AND the old key ids are revoked only after all affected records have been re-encrypted and verified
- AND no tokenized record remains readable only by a revoked key

#### Scenario: Revoked old key cannot decrypt

- GIVEN old key ids `k1`/`k2` have been revoked after re-encryption completed
- WHEN a decryption is attempted that would resolve to a revoked key id
- THEN the operation fails and no plaintext PAN is produced by a revoked key

#### Scenario: Re-encryption emits audit events without card-sensitive data

- GIVEN the re-encryption workflow processes a batch of vault records after rotation
- WHEN a batch completes
- THEN CardVault publishes exactly one `VaultReencryptionBatchCompleted` event per batch via the outbox
- AND publishes a `VaultKeyRotated` event for the rotation
- AND no event payload contains key material, plaintext PAN, or any card-sensitive value

#### Scenario: Revocation is not performed if re-encryption is incomplete

- GIVEN at least one affected record has not yet been re-encrypted under the new key
- WHEN revocation of the old key ids is attempted
- THEN revocation does not proceed
- AND the old key ids remain able to decrypt not-yet-migrated records until migration completes

---

### Requirement: Salted, Cost-Parameterized PIN Hashing

CardVault SHALL verify card PINs using a salted, cost-parameterized key-derivation function (Argon2id as the
interim control) with a per-PIN cryptographically random salt and tuned memory, iteration, and parallelism
cost parameters. CardVault SHALL persist the algorithm identifier, cost parameters, and salt alongside the
hash so parameters can evolve without a breaking migration. CardVault SHALL NOT store or verify any PIN using
unsalted SHA-256 after the transition. PIN material SHALL never appear in any log sink.

**Transition of existing unsalted-SHA-256 `PinHash` values (one-way-door decision — deferred to design):**
existing hashes cannot be reversed. The transition mechanism — verify-then-upgrade on next successful PIN
entry, or a forced PIN reset — is decided at design; this requirement pins that after the transition no card
remains verifiable only by the old unsalted scheme, and that any upgraded record no longer retains the old
unsalted hash. The scenarios below hold regardless of which transition mechanism design selects.

#### Scenario: New PIN is stored with a per-PIN salt and cost parameters

- GIVEN a PIN is set for a card
- WHEN CardVault stores the PIN hash
- THEN the stored record includes an algorithm identifier of `Argon2id`
- AND a cryptographically random per-PIN salt
- AND the memory, iteration, and parallelism cost parameters used
- AND the stored value is NOT an unsalted SHA-256 of the PIN

#### Scenario: Identical PINs on different cards do not produce identical hashes

- GIVEN two cards are assigned the same PIN value
- WHEN each PIN is hashed and stored
- THEN the two stored hash values differ (because each uses a distinct random salt)

#### Scenario: Correct PIN verifies, incorrect PIN is rejected

- GIVEN a card whose PIN was stored via the salted KDF
- WHEN the correct PIN is presented
- THEN verification succeeds
- AND WHEN an incorrect PIN is presented, verification fails

#### Scenario: PIN material is never logged

- GIVEN any PIN set or verify operation, including error and debug paths
- WHEN the operation runs
- THEN no log sink contains the plaintext PIN, nor a raw or Base64/hex encoding of the PIN or its salted input

#### Scenario: After transition, no card is verifiable only by unsalted SHA-256

- GIVEN a card previously stored with an unsalted SHA-256 `PinHash`
- WHEN the design-selected transition mechanism has been applied to that card (verify-then-upgrade or forced reset)
- THEN the card's PIN is verifiable via the salted KDF
- AND the card is NOT verifiable via the old unsalted SHA-256 scheme
- AND the upgraded record no longer retains the old unsalted hash

---

## Out-of-Scope Confirmations

- HSM-backed PIN verification (the definitive replacement for the interim KDF) — Phase 1.
- Changes to detokenization authorization or rate limiting — unchanged from the base spec.
- The git-history scrub of the leaked vault keys is an operational step tracked by SEC-01/security-hardening,
  not a vault-runtime behavioral contract.
