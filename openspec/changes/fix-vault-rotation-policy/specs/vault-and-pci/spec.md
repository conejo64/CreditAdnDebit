# Delta Spec — vault-and-pci
# Change: fix-vault-rotation-policy
# Base spec: openspec/specs/vault-and-pci/spec.md

---

## MODIFIED Requirements

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

---

### Requirement: PCI-Safe Audit Events
The system MUST publish PCI-safe audit information for sensitive vault operations.
Every successful key rotation SHALL emit a `VaultKeyRotated` event containing `actor`, `keyId` (identifier only — never key material or PAN), `traceId`, and UTC timestamp.
Every completed re-encryption batch SHALL emit a `VaultReencryptionBatchCompleted` event containing `actor`, `traceId`, `recordsAffected`, and UTC timestamp.
Audit events SHALL be published through the existing `IEventBus` / outbox path so that audit persistence is transactional with the rotation state change.
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
