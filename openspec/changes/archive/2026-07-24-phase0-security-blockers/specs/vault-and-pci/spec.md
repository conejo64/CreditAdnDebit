# Delta Spec — Phase 0 Security Blockers
# Capability: vault-and-pci
# Change: phase0-security-blockers
# Base spec: openspec/specs/vault-and-pci/spec.md

This document records ONLY what changes. It describes the WHAT (behavioral contracts), not the HOW.
It ADDS a rotated-key re-encryption + old-key-revocation requirement (SEC-01) that reuses the base spec's
existing "Vault Key Rotation" and "PCI-Safe Audit Events" contracts (`VaultKeyRotated` /
`VaultReencryptionBatchCompleted`), and ADDS a salted, cost-parameterized PIN-hashing requirement (SEC-02)
that supersedes the base spec's out-of-scope note "PIN hashing upgrade (BCrypt/Argon2id) — later wave".
Unchanged tokenization, detokenization, and audit behavior is not repeated.

---

## Supersession Note

The base security-hardening spec's out-of-scope confirmation "PIN hashing upgrade (BCrypt/Argon2id) — later
wave; blocked on data migration + forced reset" is superseded by SEC-14 below: salted, cost-parameterized PIN
hashing is now IN SCOPE as the interim control. HSM-backed PIN verification remains deferred to Phase 1.

---

## ADDED Requirements

### Requirement: Re-Encryption Under Rotated Key With Old-Key Revocation (SEC-01)

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

### Requirement: Salted, Cost-Parameterized PIN Hashing (SEC-02, Argon2id interim)

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

## Out-of-Scope Confirmations (Phase 0)

- HSM-backed PIN verification (the definitive replacement for the interim KDF) — Phase 1.
- Changes to detokenization authorization or rate limiting — unchanged from the base spec.
- The git-history scrub of the leaked vault keys is an operational step tracked by SEC-01/security-hardening,
  not a vault-runtime behavioral contract.
