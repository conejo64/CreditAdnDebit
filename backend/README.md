# CardSwitchPlatform (.NET 9) - VS 2026

Microservicios:
- **CardVault.Api**: Identity + JWT + MFA (TOTP) + Refresh token rotation + RoutingRules + EF Outbox -> Kafka.
- **IsoSwitch.Api**: Consumer Kafka (cv.routing.updated) -> RoutingRulesCache + ISO8583 demo packager + TCP simulator.

## Requisitos
- .NET SDK 9
- Docker Desktop

## Levantar infraestructura (Kafka + Postgres + SQL Server)
```bash
docker compose -f deploy/docker-compose.yml up -d
```

## Ejecutar (Visual Studio)
Abrir `CardSwitchPlatform.sln` y ejecutar:
- CardVault.Api -> http://localhost:5101/swagger
- IsoSwitch.Api -> http://localhost:5201/swagger

## Usuario Admin por defecto (seed)
- Email: admin@demo.com
- Password: Admin1234!

## Prueba rapida (end-to-end)
1) En Swagger de CardVault:
   - POST /api/auth/login con admin@demo.com / Admin1234!
   - Copia accessToken
2) En Swagger de CardVault:
   - Authorize (boton candado) con "Bearer <token>"
   - POST /api/routing-rules (crea una regla) -> publicara evento a Kafka via Outbox
3) En logs de IsoSwitch veras el evento consumido y persistido en RoutingRulesCache
4) En Swagger de IsoSwitch:
   - POST /api/iso/authorize -> envia MTI 0100 al simulador TCP y recibe 0110 rc=00

## Notas
- En Development se usa EnsureCreated() para crear DBs automaticamente. Para production se ejecuta Migrate().
- El packager ISO8583 es un "demo simple" para probar plumbing. Se reemplaza por packagers por adquirente.

## EF Migrations (reales)
> Ya incluimos migracion inicial para Postgres (CardVaultDbContext e IsoSwitchDbContext).
> Para Identity (SQL Server) puedes generar migraciones con dotnet ef si deseas versionarlas.

### CardVault (Postgres)
```bash
dotnet ef database update --project services/CardVault/src/CardVault.Infrastructure.Persistence --startup-project services/CardVault/src/CardVault.Api
```

### IsoSwitch (Postgres)
```bash
dotnet ef database update --project services/IsoSwitch/src/IsoSwitch.Infrastructure.Persistence --startup-project services/IsoSwitch/src/IsoSwitch.Api
```

### Identity (SQL Server) - opcional (recomendado en prod)
```bash
dotnet ef migrations add InitialIdentity --project services/CardVault/src/CardVault.Infrastructure.Identity --startup-project services/CardVault/src/CardVault.Api
dotnet ef database update --project services/CardVault/src/CardVault.Infrastructure.Identity --startup-project services/CardVault/src/CardVault.Api
```

## v7 - Catalog Sync + BIN validation
### Kafka topics adicionales
- `cv.catalog.country.upserted`
- `cv.catalog.binrange.upserted`
- `cv.catalog.cardproduct.upserted`

CardVault publica estos eventos via Outbox. IsoSwitch los consume y materializa cache local.

### Endpoints IsoSwitch (caches)
- `GET /api/catalog/cache/bins`
- `GET /api/catalog/cache/countries`
- `GET /api/catalog/cache/card-products`

### Validacion de BIN
`POST /api/iso/authorize` ahora requiere que el BIN exista en `BinRangesCache` (materializado desde CardVault).  
Si no existe, responde **400** con `BIN not found in catalog`.

## v9 - ISO8583 por conector + EMV55 + SwitchEvents

### Nuevos componentes
- `PackagerRegistry` + `IIso8583Packager` (listo para packagers por adquirente)
- Field 55 (EMV) como **LLLVAR** (3 digitos) y helper `EmvTlv`
- Placeholders: Field 52 (PIN block) y Field 64 (MAC) con `MacService`
- `SwitchEventPublisher` publica `sw.tx.updated` a Kafka (config en appsettings.Development.json)
- Reversal retry policy: `ReversalAttempts` (max 3) con backoff 10s/30s/60s

### Kafka
- Topic: `sw.tx.updated` (key = traceId, value JSON con status/decision/responseCode)


## v11 - Capture + Network Mgmt + Kafka topics separados

- Endpoints: /api/iso/capture, /api/iso/network/ping
- Kafka topics: TxEvents=sw.tx.events, IsoEvents=sw.iso.events


## v12 - Reversal Advice + Field 90 + SignOn/Off

- Endpoint: /api/iso/reversal-advice (0420/0430)
- Field 90: Original Data Elements (demo builder)
- Endpoints: /api/iso/network/signon y /api/iso/network/signoff


## v13 - Field 90 configurable + Reversal Advice con persistencia

- Field 90: Builder usa Iso:AcqInstId y Iso:FwdInstId
- Reversal Advice (0420/0430): fields 7/11/37 y persiste tx REVERSAL_ADVICE (PENDING->CONFIRMED)


## v15 - Idempotencia + StateMachine + Query transacciones

- Header Idempotency-Key soportado en authorize/capture/reversal-advice
- Indice unico (IdempotencyKey, TxType)
- TransactionStateMachine valida transiciones
- GET /api/transactions con filtros


## v16 - ISO8583 spec/codec mejorado (campos comunes + validacion)

- Iso8583Spec: DataType + MaxLength + nuevos campos (14/22/25/35/54/60/61/62)
- Iso8583Codec: validacion por tipo + LLVAR/LLLVAR + padding
- API: Authorize/Capture aceptan campos ISO opcionales (demo)


## v17 - Conector TCP/TLS real + retries + circuit breaker

- Nuevo TcpGatewayConnector (ConnectorId=TCP_GATEWAY) configurable en appsettings (Connectors:TcpGateway)
- TcpIsoClient reescrito: framing 2-byte length prefix, soporte TLS (SslStream), timeout, retry y circuit breaker simple
- SimulatorConnector ahora usa TcpIsoClient + PackagerRegistry


## v19 - Vault key rotation + permissions + rate limiting

- Vault: ActiveKeyId + Keys (multi-key) y decrypt por KeyId
- CanDetokenize: claim perm=vault:detokenize (o role Admin)
- RateLimiter: vault_detokenize (20 req/min)


## v20 - Vault rotation endpoints + batch re-encrypt

- Endpoints (Admin): /api/vault/active-key, /api/vault/rotate-active-key, /api/vault/reencrypt
- Rate limit: vault_admin_ops (5 req/min)
- VaultCrypto: SetActiveKeyId(keyId)
- TokenVaultService: ReEncryptBatchAsync(take)


## v21 - Persisted ActiveKeyId + background re-encrypt job + vault health

- VaultSettings table stores ActiveKeyId and last job state
- VaultStartupInitializer loads persisted ActiveKeyId into VaultCrypto
- VaultReencryptHostedService runs batch re-encrypt periodically (VaultJob config)
- /health/vault (Admin) shows vault state


## v22 - PCI-DSS base (request-id, redaction/masking, PCI audit stream)

- Middleware: X-Request-Id sets HttpContext.TraceIdentifier and adds logging scope
- PciOptions: LogSensitiveData=false, MaskPanLevel=FIRST6_LAST4
- PciAuditPublisher publishes to Kafka topic sw.audit.pci (no PAN)
- TokenVaultService publishes PCI audit events for tokenize/detokenize/rotate/reencrypt


## v23 - IsoSwitch ISO8583 tools (parse/build)

- IsoSwitch.Api exposes /api/iso/build and /api/iso/parse
- Iso8583Codec: helpers ComputePrimaryBitmapHex/ComputeSecondaryBitmapHexOrNull for diagnostics


## v24 - Catalogs + Routing rules V2 + Routing decision

- New catalogs: currencies, networks, participants (issuer/acquirer)
- RoutingRulesV2: optional dimensions (CountryCode/Network/TxType)
- Endpoint: POST /api/routing/decision
- IsoSwitch catalog and routing endpoints now require CardVault-issued JWT authorization with the admin routing policy


## v26 - Observability (OpenTelemetry + Prometheus + Jaeger)

- OpenTelemetry tracing + metrics in CardVault.Api and IsoSwitch.Api
- Traces: OTLP -> Jaeger (docker-compose.observability.yml), optional console exporter
- Metrics: /metrics Prometheus scraping endpoint
- Added docker-compose.observability.yml and observability/prometheus.yml


## v27 - Kafka trace propagation + correlated consumer

- Kafka producer injects W3C trace context (traceparent/tracestate) into headers
- Kafka consumer extracts trace context and starts a consumer span
- Demo consumer: IsoSwitch.Api.Consumers.PciAuditConsumer consumes sw.audit.pci and logs correlated traceId


## v28 - Kafka security (HMAC) + Retry/DLQ + Audit event store

- Kafka message signing: HMAC-SHA256 header x-signature (secret: Kafka:SigningSecret)
- Consumer verifies signature; invalid signature -> DLQ
- Retry/DLQ: handler exceptions publish to <topic>.retry up to Kafka:Retry:Max then <topic>.dlq
- AuditEvents table (CardVault + IsoSwitch) with PCI-safe payload hashes; endpoints GET /api/audit/latest?take=50


## v29 - Retry republisher + Kafka alerting metrics

- Added KafkaRetryRepublisherWorker (consumes <topic>.retry, exponential backoff, republishes to original topic)
- Added KafkaMetrics meter (kafka_retry_published_total, kafka_dlq_published_total, kafka_retry_republished_total, kafka_retry_backoff_ms)
- Prometheus alert rules (observability/alerts.yml) wired in docker-compose.observability.yml


## v30 - Issuer Core (Customer / Account / Card issuance + lifecycle)

- New Issuer entities (CardVault):
  - Customers, Accounts (Debit/Credit), Cards, CardStatusHistory
- Endpoints (CardVault.Api):
  - POST /api/issuer/customers
  - GET /api/issuer/customers/{id}
  - GET /api/issuer/customers?q=...&take=50
  - POST /api/issuer/accounts
  - POST /api/issuer/cards/issue   (PAN input, returns token+masked only)
  - POST /api/issuer/cards/{id}/activate
  - POST /api/issuer/cards/{id}/block
  - GET /api/issuer/cards/{id}
- Audit events are written for issuer actions (customer/account/card)

- New Issuer entities (CardVault):
  - Customers, Accounts (Debit/Credit), Cards, CardStatusHistory
- Endpoints (CardVault.Api):
  - POST /api/issuer/customers
  - GET /api/issuer/customers/{id}
  - GET /api/issuer/customers?q=...&take=50
  - POST /api/issuer/accounts
  - POST /api/issuer/cards/issue   (PAN input, returns token+masked only)
  - POST /api/issuer/cards/{id}/activate
  - POST /api/issuer/cards/{id}/block
  - GET /api/issuer/cards/{id}
- Audit events are written for issuer actions (customer/account/card)


## v31 - Ledger + Billing + Statements (min payment)

- LedgerEntries: purchases/payments/fees/interests (signed amounts; payments are negative).
- CreditPolicies: parametrizable min payment policy per ProductCode.
- Statements + StatementLines:
  - Generate statement for an account and a billing cycle.
  - Computes PreviousBalance, Purchases, Payments, Fees, Interest, NewBalance.
  - Computes MinimumPayment using policy: max(absolute, percent*NewBalance), capped to NewBalance.
- Endpoints:
  - PUT /api/credit/policies
  - POST /api/ledger/purchase | /payment | /fee | /interest
  - GET /api/ledger/accounts/{accountId}/balance
  - POST /api/billing/statements/generate
  - GET /api/billing/statements/{id}
  - GET /api/billing/accounts/{accountId}/statements


## v32 - Interest (ADB) + Statement Payments + Late Fee

- Interest (ADB): when generating a statement, if no Interest entries exist in cycle,
  the system computes interest using Average Daily Balance and policy APR, and adds a system interest entry.
- Statement payments:
  - POST /api/billing/statements/{id}/pay (tracks PaidAmount and posts a ledger payment)
- Late fee:
  - POST /api/billing/statements/{id}/latefee?force=bool
  - POST /api/billing/latefees/run?force=bool  (batch)
- Print endpoint:
  - GET /api/billing/statements/{id}/print (text/plain)


## v33 - Statement PDF + Outbox events (Kafka)

- PDF statement:
  - GET /api/billing/statements/{id}/pdf  (QuestPDF)
- Outbox events emitted by billing:
  - billing.statement.generated
  - billing.statement.payment_applied
  - billing.statement.late_fee_applied
- Outbox inspection:
  - GET /api/outbox/latest?take=50
- The EfOutboxPublisher background service publishes outbox messages to Kafka via Confluent.Kafka (KafkaEventBus).


## v34 - Switch Events (v1 schemas) -> Ledger + Daily Settlement

- Versioned events:
  - switch.v1.purchase.approved / switch.v1.purchase.reversed
  - billing.v1.* and settlement.v1.batch.created
- CardVault consumes switch events from Kafka (topic sw.tx.events) and posts Ledger entries.
  - Approved -> Purchase
  - Reversed -> Adjustment (negative)
- IsoSwitch API adds simulation endpoints publishing versioned envelopes to Kafka:
  - POST /api/simulate/purchase/approve
  - POST /api/simulate/purchase/reverse
- Settlement (simplified):
  - POST /api/settlement/run?network=Visa&businessDate=2026-01-05
  - GET /api/settlement/batches
  - GET /api/settlement/batches/{id}


## v35 - Idempotency (MTI+STAN+RRN) + Refund/Chargeback + Reconciliation

- Switch idempotency:
  - TxnJournal table unique by (Network, Mti, Stan, Rrn)
  - CardVault SwitchTxnConsumer checks journal and ignores duplicates.
- New switch events:
  - switch.v1.refund.posted
  - switch.v1.chargeback.posted (creates DisputeCase)
- IsoSwitch simulation endpoints:
  - POST /api/simulate/refund
  - POST /api/simulate/chargeback
  - All simulate endpoints accept mti/stan/rrn (optional)
- APIs:
  - GET /api/switch/journal?take=100
  - GET /api/disputes/accounts/{accountId}?take=50
  - POST /api/disputes/{id}/close?won=true|false
  - GET /api/reconciliation/settlement/{batchId}


## v36 - Partial Refund Cap + Dispute Lifecycle Events

- Partial refunds:
  - RefundRecords table tracks partial refunds by (Network, Rrn, Stan) unique.
  - Consumer enforces: sum(refunds for network+rrn) <= original purchase amount.
- Dispute lifecycle:
  - DisputeStatus now includes Representment, PreArbitration, Arbitration.
  - DisputeEvents history table records actions (open/representment/prearb/arbitration/close).
  - API: POST /api/disputes/{id}/transition  body { action, notes }
  - API: GET /api/disputes/{id}/events


## v37 - Payment Allocation Waterfall + Buckets

- Payment allocation:
  - Statement has buckets: InterestDue, FeesDue, PrincipalDue plus paid breakdown.
  - PaymentAllocatorService allocates payments Interest -> Fees -> Principal.
  - Endpoint /api/billing/statements/{id}/pay returns allocation result.
- Buckets visibility:
  - GET /api/billing/statements/{id}/buckets
- Totals:
  - BillingService recomputes TotalPaymentDue/NewBalance from remaining buckets (when initialized).


## v38 - Minimum Payment Policy (Parametrizable)

- Minimum payment:
  - Table MinimumPaymentPolicies (DEFAULT seeded).
  - MinimumPaymentService calculates:
    - principal component = max(FloorAmount, PrincipalPercent * PrincipalDue)
    - + InterestDue (optional) + FeesDue (optional)
    - capped by CeilingAmount (optional), never exceeds TotalPaymentDue.
  - Auto recalculated:
    - on statement generation
    - on payment posting
    - on late fee application
- Endpoints:
  - GET  /api/billing/policies/minimum-payment
  - POST /api/billing/policies/minimum-payment (upsert by code, can set default)
  - POST /api/billing/statements/{id}/recalculate


## v39 - Daily Interest Accrual (APR segments)

- Daily interest accrual engine (demo):
  - DailyInterestAccrualService posts daily INTEREST ledger entries for credit accounts.
  - Stores InterestAccrualRecords with base balance, APR, daily rate, and ledger link.
  - Idempotent per (AccountId, AccrualDate, Segment).
  - Demo grace: if previous balance before 'from' <= 0, skips interest first PurchaseGraceDays days.
- CreditPolicies extended:
  - PurchaseApr, CashAdvanceApr, PenaltyApr, PurchaseGraceDays.
- Endpoints:
  - POST /api/interest/accrue?accountId=...&from=YYYY-MM-DD&to=YYYY-MM-DD
  - GET  /api/interest/accruals?accountId=...&take=50
- Statement generation:
  - Uses Interest ledger entries within cycle as InterestAccrued/InterestDue.


## v40 - Fees (Overlimit / Annual / Cash Advance) + Posting Rules

- CreditPolicies additional fee fields:
  - OverlimitFee, OverlimitFeeOncePerDay
  - AnnualFee
  - CashAdvanceFeeFixed, CashAdvanceFeePercent
- FeeAssessments table:
  - Idempotency per (AccountId, FeeType, BusinessDate)
- FeeService:
  - AssessOverlimitAsync: checks balance (excluding Interest) > creditLimit and posts FEE - Overlimit
  - AssessAnnualAsync: posts FEE - Annual (once per businessDate)
  - AssessCashAdvanceAsync: posts fee = fixed + percent*cashAmount (once per businessDate)
- Posting rule:
  - CardVault SwitchTxnConsumer auto-assesses Overlimit fee after posting a purchase approval.
- Statement buckets:
  - FeesDue now initialized as all cycle fees (st.Fees), not only LateFee.
- Endpoints:
  - POST /api/fees/overlimit?accountId=...&businessDate=YYYY-MM-DD
  - POST /api/fees/annual?accountId=...&businessDate=YYYY-MM-DD
  - POST /api/fees/cash-advance?accountId=...&cashAmount=...&businessDate=YYYY-MM-DD


## v42 - PreAuth (Holds) + Clearing + ISO8583 Field 90 matching + Disputes

- PreAuth/Holds:
  - AuthorizationHolds table with Status (Active/Captured/Released).
  - Events:
    - switch.v1.auth.approved -> creates AuthorizationHold + ledger AuthorizationHold entry
    - switch.v1.auth.reversed -> releases hold (posts Reversal negative)
    - switch.v1.clearing.posted -> captures hold (posts Clearing positive)
  - Matching: STAN/RRN (demo) and optional OriginalDataElements90 (ISO8583 field 90).
- Disputes/Chargeback:
  - DisputeCases table + DisputeService
  - switch.v1.chargeback.posted -> opens dispute + posts provisional credit (Chargeback negative)
  - Resolve Lost -> re-debit via Adjustment
- Statement impact:
  - Holds are excluded from statement purchases (only Clearing/Purchase/Refund/Reversal/Chargeback/Adjustment included).
  - When a transaction falls within an Open statement cycle, we recalc aggregates + MinimumPayment.
- Endpoints:
  - GET /api/holds?accountId=...&take=50
  - GET /api/disputes?accountId=...&take=50
  - POST /api/disputes/{id}/resolve
- IsoSwitch simulation:
  - POST /api/simulate/auth/approve
  - POST /api/simulate/auth/reverse
  - POST /api/simulate/clearing


## v43 - Hold expiration + Available Credit

- Hold expiration:
  - AuthorizationHolds gains ExpiresOn column.
  - Hold TTL configured per CreditPolicy (HoldTtlHours, default 72h).
  - HoldMaintenanceService expires due holds:
    - Marks hold Expired
    - Posts Reversal ledger entry (negative) to release reserved amount.
  - HoldExpiryWorker runs every 60 seconds (demo). Also manual endpoint:
    - POST /api/holds/expire/run
- Available credit:
  - AvailableCreditService computes:
    CreditLimit - PostedBalance(excluding AuthorizationHold entries) - ActiveHolds
  - Endpoint:
    - GET /api/issuer/accounts/{id}/available-credit


## v44 - Partial capture + MCC risk rules

- Partial capture:
  - AuthorizationHolds gains CapturedAmount.
  - Capture (clearing) can be < hold amount:
    - HoldStatus becomes PartiallyCaptured until CapturedAmount >= Amount.
  - Release/Expire reverses remaining (Amount - CapturedAmount).
  - Available credit subtracts remaining holds (Amount - CapturedAmount) for Active/PartiallyCaptured.
- MCC risk rules:
  - MccRules table (Mcc, IsBlocked, PerTxnLimit).
  - RiskDecisionService checks MCC + available credit + policy AllowOverlimit/FloorLimit.
  - For auth.approved events, CardVault may mark journal as declined and not create a hold.
  - Endpoints:
    - GET/POST/DELETE /api/risk/mcc-rules


## v45 - Velocity rules + auth.declined responses

- Velocity rules:
  - VelocityRules table per ProductCode (WindowMinutes, MaxCount, MaxAmount).
  - Endpoints:
    - GET /api/risk/velocity-rules?productCode=...
    - POST /api/risk/velocity-rules
    - DELETE /api/risk/velocity-rules/{id}
  - RiskDecisionService checks recent TxnJournal Authorization entries within window.
- Switch responses (bidirectional concept):
  - When an auth is declined (MCC/velocity/available credit), CardVault publishes:
    - eventName: switch.v1.auth.declined
    - payload includes responseCode (demo "05") + reason + STAN/RRN
  - IsoSwitch has SwitchResponseConsumer subscribed to topic:
    - Kafka:SwitchResponseTopic (default switch-responses)
  - Endpoint:
    - GET /api/responses?take=...


## v46 - ISO8583 0110 response (bitmap + fields) (Option B)

- CardVault publishes switch.v1.auth.response (approved/declined) to Kafka topic (default switch-responses).
- IsoSwitch consumes switch responses and builds a REAL ISO8583 auth response message:
  - MTI 0110
  - Primary bitmap (DE3,4,7,11,12,13,37,38,39,49)
  - Packed as ASCII then exposed as HEX string for inspection.
- Endpoint:
  - GET /api/responses?take=... returns recent items including ISO0110_HEX=...


## v47 - TCP ISO8583 0100 -> 0110 (Option B)

- TCP server (IsoSwitch):
  - Port: Tcp:Iso8583Port (default 7000)
  - Protocol: one ASCII ISO message per line (newline-terminated)
  - Format: MTI(4) + PrimaryBitmapHex(16) + fields (subset) in ascending order
- Unpacker:
  - Iso8583Unpacker parses 0100 and extracts DE2 (PAN), DE4 (amount), DE11 (STAN), DE37 (RRN), optional DE18 (MCC), DE42 (AcceptorId).
- Demo PAN mapping:
  - POST /api/demo/pan-map to map PAN -> AccountId for routing to CardVault.
- Correlated synchronous response:
  - IsoSwitch registers awaiter by STAN|RRN, publishes switch.v1.auth.approved to Kafka, waits auth.response from CardVault, returns ISO 0110 ASCII over TCP.
- Helper:
  - GET /api/demo/iso0100 returns sample ISO0100 ASCII+HEX for testing.


## v48 - TCP length-prefix framing + binary bitmap + 0200/0210

- TCP ISO8583 server now uses 2-byte big-endian length prefix framing.
- Payload format: MTI(4 ASCII) + bitmap (8 bytes binary) + fields.
- Unpacker: supports DE2,3,4,11,18,37,41,42,49.
- Responses:
  - 0100 -> 0110 (binary bitmap) with DE39 mapped from CardVault decision.
  - 0200 -> 0210 (demo 00) after publishing clearing event.
- Demo endpoints:
  - GET /api/demo/iso0100/binary -> returns payloadHex + frameHex
  - POST /api/demo/tcp-send -> sends frameHex to TCP server and returns response payloadHex


## v49 - TPDU optional + echo DE7/12/13 + ISO traces + DE39 mapping

- IsoSwitch TCP:
  - Optional TPDU (5 bytes) controlled by Tcp:UseTpdu=true.
  - Echo DE7/12/13/18/41/42/49 when present.
  - In-memory trace store (last 500):
    - GET /api/iso/traces?take=...
- Demo:
  - GET /api/demo/iso0100/binary-tpdu -> creates a framed 0100 with TPDU (default 6000030000).
- CardVault:
  - Maps decline reasons -> DE39 response codes:
    - 51 insufficient funds/credit
    - 65 velocity exceeded
    - 59 suspected fraud
    - 62 restricted/MCC/policy
    - 05 default do not honor


## v50 - BIN routing + DE35/DE52/DE55 (simulated)

- BIN routing catalog (IsoSwitch):
  - GET /api/catalog/bin-routes
  - POST /api/catalog/bin-routes { bin6, network, currency }
  - Used to route `network` (VISA/MASTERCARD/DISCOVER/DINERS) from PAN BIN6.
- ISO8583 extras (demo):
  - DE35 Track2 (LLVAR)
  - DE52 PIN block (fixed 16 chars demo)
  - DE55 EMV ICC data (LLLVAR demo)
- Demo frame generators:
  - GET /api/demo/iso0100/binary-v50
  - GET /api/demo/iso0100/binary-tpdu-v50
  (Both allow query params: track2, pinBlock, emv55)


## v51 - Luhn validation + internal token PAN + file persistence

- PAN validation (IsoSwitch):
  - Luhn check; invalid PAN returns DE39=14.
- Internal tokenization:
  - Deterministic TokenPAN: TPAN_<HMACSHA256(secret, pan) first 24 hex>.
  - Endpoints:
    - POST /api/tokenization/tokenize { pan } -> tokenPan + masked + bin6/last4
    - POST /api/demo/pan-map-v51 { pan, accountId } -> maps tokenPan -> accountId
    - POST /api/demo/pan-map-token { tokenPan, accountId }
- Persistence (file-based):
  - BIN routes saved to `data/binroutes.json`
  - ISO traces appended to `data/isotraces.jsonl`
  - Configure via `Persistence:BaseDir` (defaults to ./data)


## v52 - DB persistence (Postgres) for catalogs + audit logs

- Postgres persistence:
  - Connection string name: `ConnectionStrings:Postgres`
  - Migrations applied by `DbMigrateWorker` on startup.
- Catalog persistence via `AuditEvents`:
  - BIN routes upserts stored as `catalog.binroute.upserted`
  - TokenPAN mappings stored as `tokenpan.mapped`
  - On startup, stores are rebuilt by replaying audit events.
- ISO message/audit persistence:
  - Binary ISO frames logged to `IsoMessageLogs` (PCI-safe JSON; PAN/PIN/EMV masked)
  - Endpoint: GET /api/iso/audit/logs?take=50


## v54 - IsoAudit microservice (Kafka consumer) for ISO audit logs

- New service: IsoAudit.Api
  - Consumes Kafka topic `sw.iso.audit` (eventName: iso.audit.v1)
  - Persists to Postgres table `iso_message_logs`
  - Endpoints:
    - GET http://localhost:5301/health
    - GET http://localhost:5301/api/audit/logs?take=50
- IsoSwitch changes:
  - BinaryIsoAuditService publishes audit envelope to Kafka via SwitchEventPublisher.PublishAuditAsync
  - Config: Audit:WriteToDb=false by default (audit is stored by IsoAudit service)
  - Config: Kafka:Topics:AuditEvents=sw.iso.audit


## v55 - IsoAudit idempotency + enriched audit payload + JWT protection

- IsoAudit idempotency:
  - Unique index: (TraceId, Direction) in `iso_message_logs`
  - Consumer stores deterministic Id derived from traceId|direction|mti and ignores duplicates.
- Enriched audit events:
  - IsoSwitch OUT audit includes { tokenPan, panMasked, network, currency } (PCI-safe)
  - IN audit includes terminal/acceptor/mcc/amount/currency
- IsoAudit security:
  - JWT bearer required for GET /api/audit/logs
  - Policy: scope `audit.read` OR role `Admin`
  - IsoAudit Jwt:Key aligned to CardVault Jwt:SigningKey in dev for quick testing.
