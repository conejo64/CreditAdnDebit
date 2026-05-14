# Modelo de Base de Datos
## Core Bancario de Tarjetas de Crédito y Débito

Este documento define una propuesta de modelo de base de datos para un **Core Bancario de Tarjetas** con enfoque de banco real, cubriendo:

- clientes
- cuentas
- productos
- tarjetas
- autorizaciones
- transacciones
- compensación
- facturación
- pagos
- fraude
- reclamos
- contabilidad
- auditoría

---

# 1. Consideraciones generales

## Objetivo

Diseñar una base de datos modular y escalable para soportar:

- tarjetas de crédito
- tarjetas de débito
- switch transaccional
- autorizaciones en línea
- conciliación y liquidación
- estados de cuenta
- pagos y reversos
- antifraude
- disputas y contracargos
- integración contable

## Convenciones sugeridas

- PK tipo `UUID`
- campos de auditoría en tablas críticas:
  - CreatedAt
  - UpdatedAt
  - CreatedBy
  - UpdatedBy
- catálogos parametrizables para estados, tipos, códigos y canales
- soft delete solo si el negocio lo requiere
- cifrado o tokenización para datos sensibles
- PAN completo nunca expuesto en texto plano en ambientes no seguros

---

# 2. Dominios principales

- Customer
- Account
- Card Product
- Card
- Authorization
- Transaction
- Clearing / Settlement
- Statement
- Payment
- Fraud
- Dispute
- Ledger / Accounting
- Notification
- Audit / Security

---

# 3. Modelo de tablas

# 3.1. Clientes

## Table: Customers

**Descripción:** Información principal del cliente titular.

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| CustomerId | UUID | Identificador único |
| CustomerType | VARCHAR(20) | PERSON / COMPANY |
| IdentificationType | VARCHAR(20) | DNI, PASSPORT, RUC, etc. |
| IdentificationNumber | VARCHAR(50) | Número de identificación |
| FirstName | VARCHAR(100) | Nombres |
| LastName | VARCHAR(100) | Apellidos |
| FullName | VARCHAR(250) | Nombre completo |
| BirthDate | DATE | Fecha de nacimiento |
| Email | VARCHAR(150) | Correo |
| Phone | VARCHAR(50) | Teléfono |
| Status | VARCHAR(20) | ACTIVE, INACTIVE, BLOCKED |
| RiskLevel | VARCHAR(20) | LOW, MEDIUM, HIGH |
| CreditScore | INTEGER | Score interno |
| CreatedAt | TIMESTAMP | Fecha creación |
| UpdatedAt | TIMESTAMP | Fecha actualización |

## Table: CustomerAddresses

**Descripción:** Direcciones del cliente.

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| AddressId | UUID | PK |
| CustomerId | UUID | FK a Customers |
| AddressType | VARCHAR(20) | HOME, WORK, BILLING |
| CountryCode | VARCHAR(10) | Código país |
| Province | VARCHAR(100) | Provincia |
| City | VARCHAR(100) | Ciudad |
| MainStreet | VARCHAR(150) | Calle principal |
| SecondaryStreet | VARCHAR(150) | Calle secundaria |
| Reference | VARCHAR(250) | Referencia |
| IsPrimary | BOOLEAN | Dirección principal |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: CustomerContacts

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| ContactId | UUID | PK |
| CustomerId | UUID | FK a Customers |
| ContactType | VARCHAR(20) | EMAIL, MOBILE, PHONE |
| ContactValue | VARCHAR(150) | Valor contacto |
| IsVerified | BOOLEAN | Verificado |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: CustomerSegments

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| SegmentId | UUID | PK |
| CustomerId | UUID | FK a Customers |
| SegmentCode | VARCHAR(30) | Código segmento |
| SegmentName | VARCHAR(100) | Nombre segmento |
| AssignedAt | TIMESTAMP | Fecha asignación |

---

# 3.2. Cuentas

## Table: Accounts

**Descripción:** Cuenta bancaria o cuenta asociada a la tarjeta.

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| AccountId | UUID | PK |
| CustomerId | UUID | FK a Customers |
| AccountNumber | VARCHAR(50) | Número de cuenta |
| AccountType | VARCHAR(30) | SAVINGS, CHECKING, CREDIT_CARD |
| CurrencyCode | VARCHAR(10) | Moneda |
| AvailableBalance | DECIMAL(18,2) | Saldo disponible |
| LedgerBalance | DECIMAL(18,2) | Saldo contable |
| HoldBalance | DECIMAL(18,2) | Saldo retenido |
| Status | VARCHAR(20) | ACTIVE, BLOCKED, CLOSED |
| OpenDate | DATE | Fecha apertura |
| CloseDate | DATE | Fecha cierre |
| CreatedAt | TIMESTAMP | Fecha creación |
| UpdatedAt | TIMESTAMP | Fecha actualización |

## Table: AccountLimits

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| AccountLimitId | UUID | PK |
| AccountId | UUID | FK a Accounts |
| DailyDebitLimit | DECIMAL(18,2) | Límite diario débito |
| DailyPurchaseLimit | DECIMAL(18,2) | Límite diario compras |
| DailyAtmLimit | DECIMAL(18,2) | Límite diario ATM |
| DailyTransferLimit | DECIMAL(18,2) | Límite diario transferencias |
| CreatedAt | TIMESTAMP | Fecha creación |
| UpdatedAt | TIMESTAMP | Fecha actualización |

## Table: AccountHolds

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| HoldId | UUID | PK |
| AccountId | UUID | FK a Accounts |
| HoldType | VARCHAR(30) | AUTHORIZATION_HOLD, JUDICIAL, OTHER |
| Amount | DECIMAL(18,2) | Monto retenido |
| Reason | VARCHAR(250) | Motivo |
| Status | VARCHAR(20) | ACTIVE, RELEASED |
| CreatedAt | TIMESTAMP | Fecha creación |
| ReleasedAt | TIMESTAMP | Fecha liberación |

---

# 3.3. Productos de tarjeta

## Table: CardProducts

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| CardProductId | UUID | PK |
| ProductCode | VARCHAR(30) | Código producto |
| ProductName | VARCHAR(100) | Nombre |
| CardType | VARCHAR(20) | CREDIT / DEBIT |
| Brand | VARCHAR(20) | VISA / MASTERCARD |
| CurrencyCode | VARCHAR(10) | Moneda |
| BillingCycleDay | INTEGER | Día de corte |
| GraceDays | INTEGER | Días de gracia |
| AnnualFee | DECIMAL(18,2) | Cuota anual |
| Status | VARCHAR(20) | ACTIVE / INACTIVE |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: CardProductFees

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| FeeId | UUID | PK |
| CardProductId | UUID | FK a CardProducts |
| FeeType | VARCHAR(30) | ANNUAL, CASH_ADVANCE, LATE_FEE |
| FeeName | VARCHAR(100) | Nombre cargo |
| Amount | DECIMAL(18,2) | Valor fijo |
| Percentage | DECIMAL(10,4) | Valor porcentual |
| CurrencyCode | VARCHAR(10) | Moneda |
| EffectiveDate | DATE | Vigencia desde |
| EndDate | DATE | Vigencia hasta |

## Table: CardProductRates

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| RateId | UUID | PK |
| CardProductId | UUID | FK a CardProducts |
| RateType | VARCHAR(30) | PURCHASE, CASH_ADVANCE, REVOLVING |
| NominalRate | DECIMAL(10,4) | Tasa nominal |
| EffectiveRate | DECIMAL(10,4) | Tasa efectiva |
| EffectiveDate | DATE | Vigencia desde |
| EndDate | DATE | Vigencia hasta |

---

# 3.4. Tarjetas

## Table: Cards

**Descripción:** Registro lógico de la tarjeta.

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| CardId | UUID | PK |
| CustomerId | UUID | FK a Customers |
| AccountId | UUID | FK a Accounts |
| CardProductId | UUID | FK a CardProducts |
| PanHash | VARCHAR(255) | Hash del PAN |
| Last4 | VARCHAR(4) | Últimos 4 dígitos |
| TokenReference | VARCHAR(150) | Referencia token |
| CardType | VARCHAR(20) | CREDIT / DEBIT |
| Brand | VARCHAR(20) | VISA / MASTERCARD |
| ExpirationMonth | INTEGER | Mes expiración |
| ExpirationYear | INTEGER | Año expiración |
| CardStatus | VARCHAR(20) | CREATED, ACTIVE, BLOCKED, EXPIRED |
| EmbossedName | VARCHAR(100) | Nombre impreso |
| IsVirtual | BOOLEAN | Tarjeta virtual |
| CreatedAt | TIMESTAMP | Fecha creación |
| ActivatedAt | TIMESTAMP | Fecha activación |
| UpdatedAt | TIMESTAMP | Fecha actualización |

## Table: CardPlastics

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| PlasticId | UUID | PK |
| CardId | UUID | FK a Cards |
| SequenceNumber | INTEGER | Secuencia del plástico |
| PlasticStatus | VARCHAR(20) | PRINTED, DELIVERED, REPLACED |
| IssueReason | VARCHAR(30) | NEW, RENEWAL, REPLACEMENT |
| DeliveryChannel | VARCHAR(30) | BRANCH, COURIER |
| PrintedAt | TIMESTAMP | Fecha impresión |
| DeliveredAt | TIMESTAMP | Fecha entrega |

## Table: CardStatusHistory

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| CardStatusHistoryId | UUID | PK |
| CardId | UUID | FK a Cards |
| PreviousStatus | VARCHAR(20) | Estado anterior |
| NewStatus | VARCHAR(20) | Estado nuevo |
| ChangeReason | VARCHAR(100) | Motivo |
| ChangedBy | VARCHAR(100) | Usuario o sistema |
| ChangedAt | TIMESTAMP | Fecha cambio |

## Table: CardPins

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| CardPinId | UUID | PK |
| CardId | UUID | FK a Cards |
| PinOffset | VARCHAR(255) | Valor seguro o referencia HSM |
| RetryCounter | INTEGER | Intentos fallidos |
| IsBlocked | BOOLEAN | PIN bloqueado |
| UpdatedAt | TIMESTAMP | Fecha actualización |

## Table: CardLimits

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| CardLimitId | UUID | PK |
| CardId | UUID | FK a Cards |
| PurchaseLimitPerTxn | DECIMAL(18,2) | Límite compra por transacción |
| PurchaseDailyLimit | DECIMAL(18,2) | Límite compra diario |
| AtmLimitPerTxn | DECIMAL(18,2) | Límite ATM por transacción |
| AtmDailyLimit | DECIMAL(18,2) | Límite ATM diario |
| EcommerceDailyLimit | DECIMAL(18,2) | Límite e-commerce diario |
| ContactlessDailyLimit | DECIMAL(18,2) | Límite contactless diario |
| UpdatedAt | TIMESTAMP | Fecha actualización |

## Table: CardTokens

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| CardTokenId | UUID | PK |
| CardId | UUID | FK a Cards |
| TokenProvider | VARCHAR(50) | ApplePay, GooglePay, interno |
| TokenReference | VARCHAR(150) | Token |
| DeviceReference | VARCHAR(150) | Dispositivo |
| Status | VARCHAR(20) | ACTIVE, REVOKED |
| ProvisionedAt | TIMESTAMP | Alta token |
| RevokedAt | TIMESTAMP | Baja token |

---

# 3.5. Autorizaciones

## Table: Authorizations

**Descripción:** Solicitudes de autorización recibidas por el switch.

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| AuthorizationId | UUID | PK |
| CardId | UUID | FK a Cards |
| AccountId | UUID | FK a Accounts |
| TraceNumber | VARCHAR(20) | STAN |
| RetrievalReferenceNumber | VARCHAR(30) | RRN |
| ChannelType | VARCHAR(20) | POS, ATM, ECOMMERCE |
| MerchantId | VARCHAR(30) | Comercio |
| MerchantName | VARCHAR(150) | Nombre comercio |
| TerminalId | VARCHAR(30) | Terminal |
| AcquirerId | VARCHAR(30) | Adquirente |
| TransactionType | VARCHAR(30) | PURCHASE, CASH_ADVANCE, BALANCE_INQUIRY |
| TransactionAmount | DECIMAL(18,2) | Monto |
| BillingAmount | DECIMAL(18,2) | Monto facturable |
| CurrencyCode | VARCHAR(10) | Moneda |
| CountryCode | VARCHAR(10) | País |
| PosEntryMode | VARCHAR(20) | Entrada POS |
| ResponseCode | VARCHAR(5) | Código respuesta |
| ResponseDescription | VARCHAR(150) | Descripción |
| AuthorizationCode | VARCHAR(20) | Código auth |
| Status | VARCHAR(20) | APPROVED, DECLINED, REVERSED |
| RequestedAt | TIMESTAMP | Fecha request |
| RespondedAt | TIMESTAMP | Fecha response |

## Table: AuthorizationRequests

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| AuthorizationRequestId | UUID | PK |
| AuthorizationId | UUID | FK a Authorizations |
| RawMessage | TEXT | Mensaje crudo |
| MessageFormat | VARCHAR(20) | ISO8583, JSON, XML |
| SourceSystem | VARCHAR(50) | Sistema origen |
| ReceivedAt | TIMESTAMP | Fecha recepción |

## Table: AuthorizationResponses

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| AuthorizationResponseId | UUID | PK |
| AuthorizationId | UUID | FK a Authorizations |
| RawMessage | TEXT | Mensaje salida |
| MessageFormat | VARCHAR(20) | Formato |
| SentAt | TIMESTAMP | Fecha envío |

## Table: AuthorizationRulesAudit

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| RuleAuditId | UUID | PK |
| AuthorizationId | UUID | FK a Authorizations |
| RuleCode | VARCHAR(30) | Regla |
| RuleName | VARCHAR(100) | Nombre |
| RuleResult | VARCHAR(20) | PASS, FAIL, WARN |
| RiskScore | DECIMAL(10,2) | Puntaje |
| EvaluatedAt | TIMESTAMP | Fecha evaluación |

## Table: AuthorizationReversals

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| ReversalId | UUID | PK |
| AuthorizationId | UUID | FK a Authorizations |
| OriginalTraceNumber | VARCHAR(20) | STAN original |
| ReversalAmount | DECIMAL(18,2) | Monto |
| ReasonCode | VARCHAR(20) | Motivo |
| Status | VARCHAR(20) | PENDING, APPLIED |
| RequestedAt | TIMESTAMP | Fecha solicitud |
| AppliedAt | TIMESTAMP | Fecha aplicación |

---

# 3.6. Transacciones

## Table: Transactions

**Descripción:** Movimiento financiero generado por una autorización o proceso batch.

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| TransactionId | UUID | PK |
| AuthorizationId | UUID | FK a Authorizations |
| CardId | UUID | FK a Cards |
| AccountId | UUID | FK a Accounts |
| TransactionType | VARCHAR(30) | PURCHASE, ATM, PAYMENT, REVERSAL |
| TransactionSubType | VARCHAR(30) | ONLINE, CONTACTLESS, INSTALLMENT |
| Amount | DECIMAL(18,2) | Monto |
| CurrencyCode | VARCHAR(10) | Moneda |
| ExchangeRate | DECIMAL(18,6) | Tipo cambio |
| MerchantId | VARCHAR(30) | Comercio |
| MerchantName | VARCHAR(150) | Nombre |
| MerchantCategoryCode | VARCHAR(10) | MCC |
| City | VARCHAR(100) | Ciudad |
| CountryCode | VARCHAR(10) | País |
| TransactionDate | TIMESTAMP | Fecha transacción |
| ValueDate | DATE | Fecha valor |
| TransactionStatus | VARCHAR(20) | PENDING, POSTED, REVERSED |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: TransactionEntries

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| EntryId | UUID | PK |
| TransactionId | UUID | FK a Transactions |
| EntryType | VARCHAR(30) | HOLD, POSTING, REVERSAL |
| Amount | DECIMAL(18,2) | Monto |
| CurrencyCode | VARCHAR(10) | Moneda |
| Direction | VARCHAR(10) | DEBIT / CREDIT |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: TransactionCommissions

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| CommissionId | UUID | PK |
| TransactionId | UUID | FK a Transactions |
| CommissionType | VARCHAR(30) | ATM_FEE, CASH_ADVANCE_FEE |
| Amount | DECIMAL(18,2) | Monto |
| TaxAmount | DECIMAL(18,2) | Impuesto |
| CurrencyCode | VARCHAR(10) | Moneda |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: TransactionInstallments

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| InstallmentPlanId | UUID | PK |
| TransactionId | UUID | FK a Transactions |
| TotalInstallments | INTEGER | Total cuotas |
| CurrentInstallment | INTEGER | Cuota actual |
| InstallmentAmount | DECIMAL(18,2) | Monto cuota |
| InterestAmount | DECIMAL(18,2) | Interés |
| StartDate | DATE | Inicio |
| EndDate | DATE | Fin |
| Status | VARCHAR(20) | ACTIVE, FINISHED |

## Table: TransactionAdjustments

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| AdjustmentId | UUID | PK |
| TransactionId | UUID | FK a Transactions |
| AdjustmentType | VARCHAR(30) | MANUAL, CHARGEBACK, CORRECTION |
| Amount | DECIMAL(18,2) | Monto |
| Reason | VARCHAR(250) | Motivo |
| CreatedBy | VARCHAR(100) | Usuario |
| CreatedAt | TIMESTAMP | Fecha creación |

---

# 3.7. Compensación y liquidación

## Table: ClearingFiles

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| ClearingFileId | UUID | PK |
| NetworkCode | VARCHAR(20) | VISA, MC, etc. |
| FileName | VARCHAR(255) | Nombre archivo |
| BusinessDate | DATE | Fecha negocio |
| ReceivedAt | TIMESTAMP | Fecha recepción |
| TotalRecords | INTEGER | Total registros |
| TotalAmount | DECIMAL(18,2) | Total monto |
| Status | VARCHAR(20) | RECEIVED, PROCESSED, ERROR |

## Table: ClearingTransactions

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| ClearingTransactionId | UUID | PK |
| ClearingFileId | UUID | FK a ClearingFiles |
| TransactionId | UUID | FK a Transactions |
| ExternalReference | VARCHAR(100) | Referencia externa |
| Amount | DECIMAL(18,2) | Monto |
| CurrencyCode | VARCHAR(10) | Moneda |
| ClearingStatus | VARCHAR(20) | MATCHED, UNMATCHED |
| BusinessDate | DATE | Fecha negocio |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: SettlementBatches

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| SettlementBatchId | UUID | PK |
| NetworkCode | VARCHAR(20) | Red |
| BusinessDate | DATE | Fecha negocio |
| CurrencyCode | VARCHAR(10) | Moneda |
| GrossAmount | DECIMAL(18,2) | Bruto |
| NetAmount | DECIMAL(18,2) | Neto |
| FeeAmount | DECIMAL(18,2) | Comisiones |
| Status | VARCHAR(20) | PENDING, SETTLED |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: SettlementDetails

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| SettlementDetailId | UUID | PK |
| SettlementBatchId | UUID | FK a SettlementBatches |
| ClearingTransactionId | UUID | FK a ClearingTransactions |
| Amount | DECIMAL(18,2) | Monto |
| FeeAmount | DECIMAL(18,2) | Fee |
| NetAmount | DECIMAL(18,2) | Neto |
| PostedAt | TIMESTAMP | Fecha contabilización |

## Table: ReconciliationResults

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| ReconciliationResultId | UUID | PK |
| BusinessDate | DATE | Fecha |
| SourceType | VARCHAR(20) | INTERNAL / NETWORK |
| InternalCount | INTEGER | Conteo interno |
| ExternalCount | INTEGER | Conteo externo |
| DifferenceCount | INTEGER | Diferencias |
| DifferenceAmount | DECIMAL(18,2) | Monto diferencia |
| Status | VARCHAR(20) | OPEN, RESOLVED |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: ReconciliationItems

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| ReconciliationItemId | UUID | PK |
| ReconciliationResultId | UUID | FK a ReconciliationResults |
| TransactionId | UUID | FK a Transactions |
| ExternalReference | VARCHAR(100) | Referencia externa |
| DifferenceType | VARCHAR(30) | MISSING, DUPLICATE, AMOUNT_MISMATCH |
| InternalAmount | DECIMAL(18,2) | Monto interno |
| ExternalAmount | DECIMAL(18,2) | Monto externo |
| ResolutionStatus | VARCHAR(20) | OPEN, FIXED |
| ResolvedAt | TIMESTAMP | Fecha resolución |

---

# 3.8. Tarjeta de crédito, facturación y estados de cuenta

## Table: CreditCardAccounts

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| CreditCardAccountId | UUID | PK |
| AccountId | UUID | FK a Accounts |
| CreditLimit | DECIMAL(18,2) | Cupo total |
| AvailableCredit | DECIMAL(18,2) | Cupo disponible |
| CashAdvanceLimit | DECIMAL(18,2) | Cupo avance |
| AvailableCashAdvance | DECIMAL(18,2) | Disponible avance |
| CurrentBalance | DECIMAL(18,2) | Saldo actual |
| StatementBalance | DECIMAL(18,2) | Saldo al corte |
| MinimumPayment | DECIMAL(18,2) | Pago mínimo |
| LastPaymentDate | DATE | Último pago |
| DueDate | DATE | Fecha vencimiento |
| BillingCycleDay | INTEGER | Día corte |
| Status | VARCHAR(20) | ACTIVE, DELINQUENT |

## Table: Statements

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| StatementId | UUID | PK |
| CreditCardAccountId | UUID | FK a CreditCardAccounts |
| StatementNumber | VARCHAR(30) | Número estado |
| PeriodStartDate | DATE | Inicio periodo |
| PeriodEndDate | DATE | Fin periodo |
| CutoffDate | DATE | Fecha corte |
| DueDate | DATE | Fecha vencimiento |
| PreviousBalance | DECIMAL(18,2) | Saldo anterior |
| PurchasesAmount | DECIMAL(18,2) | Compras |
| CashAdvanceAmount | DECIMAL(18,2) | Avances |
| InterestAmount | DECIMAL(18,2) | Intereses |
| FeeAmount | DECIMAL(18,2) | Comisiones |
| TaxAmount | DECIMAL(18,2) | Impuestos |
| MinimumPayment | DECIMAL(18,2) | Pago mínimo |
| TotalPaymentDue | DECIMAL(18,2) | Total a pagar |
| GeneratedAt | TIMESTAMP | Fecha generación |

## Table: StatementLines

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| StatementLineId | UUID | PK |
| StatementId | UUID | FK a Statements |
| TransactionId | UUID | FK a Transactions |
| LineType | VARCHAR(30) | PURCHASE, FEE, INTEREST, PAYMENT |
| Description | VARCHAR(250) | Descripción |
| Amount | DECIMAL(18,2) | Monto |
| PostedAt | TIMESTAMP | Fecha contabilización |

## Table: InterestAccruals

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| InterestAccrualId | UUID | PK |
| CreditCardAccountId | UUID | FK a CreditCardAccounts |
| StatementId | UUID | FK a Statements |
| InterestType | VARCHAR(30) | REVOLVING, CASH_ADVANCE |
| PrincipalBase | DECIMAL(18,2) | Base cálculo |
| RateApplied | DECIMAL(10,4) | Tasa |
| DaysCalculated | INTEGER | Días |
| Amount | DECIMAL(18,2) | Monto |
| CalculatedAt | TIMESTAMP | Fecha cálculo |

## Table: DelinquencyCharges

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| DelinquencyChargeId | UUID | PK |
| CreditCardAccountId | UUID | FK a CreditCardAccounts |
| StatementId | UUID | FK a Statements |
| ChargeType | VARCHAR(30) | LATE_FEE, PENALTY_INTEREST |
| Amount | DECIMAL(18,2) | Monto |
| AppliedAt | TIMESTAMP | Fecha aplicación |

---

# 3.9. Pagos

## Table: Payments

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| PaymentId | UUID | PK |
| CreditCardAccountId | UUID | FK a CreditCardAccounts |
| AccountId | UUID | FK a Accounts |
| CardId | UUID | FK a Cards |
| PaymentChannel | VARCHAR(30) | CASHIER, TRANSFER, APP, AUTO_DEBIT |
| PaymentReference | VARCHAR(100) | Referencia |
| Amount | DECIMAL(18,2) | Monto |
| CurrencyCode | VARCHAR(10) | Moneda |
| PaymentDate | TIMESTAMP | Fecha pago |
| AppliedDate | TIMESTAMP | Fecha aplicación |
| PaymentStatus | VARCHAR(20) | REGISTERED, APPLIED, REVERSED |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: PaymentAllocations

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| PaymentAllocationId | UUID | PK |
| PaymentId | UUID | FK a Payments |
| AllocationType | VARCHAR(30) | LATE_FEE, INTEREST, CAPITAL |
| AllocatedAmount | DECIMAL(18,2) | Monto asignado |
| PriorityOrder | INTEGER | Prioridad |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: PaymentReversals

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| PaymentReversalId | UUID | PK |
| PaymentId | UUID | FK a Payments |
| ReversalReason | VARCHAR(150) | Motivo |
| ReversalAmount | DECIMAL(18,2) | Monto |
| ReversedAt | TIMESTAMP | Fecha reverso |
| Status | VARCHAR(20) | APPLIED, FAILED |

## Table: DirectDebitMandates

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| DirectDebitMandateId | UUID | PK |
| CustomerId | UUID | FK a Customers |
| DebitAccountId | UUID | FK a Accounts |
| CreditCardAccountId | UUID | FK a CreditCardAccounts |
| MandateStatus | VARCHAR(20) | ACTIVE, CANCELLED |
| StartDate | DATE | Inicio |
| EndDate | DATE | Fin |
| CreatedAt | TIMESTAMP | Fecha creación |

---

# 3.10. Fraude y riesgo

## Table: FraudAlerts

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| FraudAlertId | UUID | PK |
| CardId | UUID | FK a Cards |
| TransactionId | UUID | FK a Transactions |
| AlertType | VARCHAR(50) | GEO_MISMATCH, VELOCITY, DEVICE_RISK |
| RiskScore | DECIMAL(10,2) | Puntaje |
| Severity | VARCHAR(20) | LOW, MEDIUM, HIGH |
| Status | VARCHAR(20) | OPEN, CLOSED |
| TriggeredAt | TIMESTAMP | Fecha disparo |
| ClosedAt | TIMESTAMP | Fecha cierre |

## Table: FraudCases

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| FraudCaseId | UUID | PK |
| CardId | UUID | FK a Cards |
| CustomerId | UUID | FK a Customers |
| OpenedAt | TIMESTAMP | Apertura |
| ClosedAt | TIMESTAMP | Cierre |
| CaseStatus | VARCHAR(20) | OPEN, INVESTIGATING, CLOSED |
| AssignedAnalyst | VARCHAR(100) | Analista |
| FinalDecision | VARCHAR(30) | FRAUD, LEGITIMATE |

## Table: FraudCaseTransactions

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| FraudCaseTransactionId | UUID | PK |
| FraudCaseId | UUID | FK a FraudCases |
| TransactionId | UUID | FK a Transactions |
| LinkedAt | TIMESTAMP | Fecha asociación |

## Table: RiskRules

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| RiskRuleId | UUID | PK |
| RuleCode | VARCHAR(30) | Código regla |
| RuleName | VARCHAR(100) | Nombre |
| RuleType | VARCHAR(30) | VELOCITY, GEO, MCC, DEVICE |
| ConditionExpression | TEXT | Regla parametrizada |
| ActionType | VARCHAR(30) | ALERT, DECLINE, BLOCK |
| ScoreValue | DECIMAL(10,2) | Puntaje |
| Status | VARCHAR(20) | ACTIVE, INACTIVE |
| EffectiveDate | DATE | Vigencia desde |
| EndDate | DATE | Vigencia hasta |

## Table: RiskProfiles

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| RiskProfileId | UUID | PK |
| CustomerId | UUID | FK a Customers |
| CardId | UUID | FK a Cards |
| ProfileType | VARCHAR(30) | CUSTOMER, CARD |
| CountryRiskLevel | VARCHAR(20) | Riesgo país |
| BehaviorRiskLevel | VARCHAR(20) | Riesgo comportamiento |
| FraudRiskLevel | VARCHAR(20) | Riesgo fraude |
| UpdatedAt | TIMESTAMP | Fecha actualización |

## Table: DeviceFingerprints

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| DeviceFingerprintId | UUID | PK |
| CustomerId | UUID | FK a Customers |
| CardId | UUID | FK a Cards |
| DeviceId | VARCHAR(150) | Identificador dispositivo |
| DeviceType | VARCHAR(30) | MOBILE, WEB |
| OperatingSystem | VARCHAR(50) | SO |
| Browser | VARCHAR(50) | Navegador |
| RiskLevel | VARCHAR(20) | Riesgo |
| LastSeenAt | TIMESTAMP | Última vez visto |

---

# 3.11. Reclamos y contracargos

## Table: Disputes

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| DisputeId | UUID | PK |
| TransactionId | UUID | FK a Transactions |
| CardId | UUID | FK a Cards |
| CustomerId | UUID | FK a Customers |
| DisputeType | VARCHAR(30) | UNRECOGNIZED, DUPLICATE, SERVICE_ISSUE |
| DisputeReasonCode | VARCHAR(20) | Código motivo |
| Description | VARCHAR(500) | Descripción |
| OpenDate | TIMESTAMP | Apertura |
| CloseDate | TIMESTAMP | Cierre |
| Status | VARCHAR(20) | OPEN, IN_PROGRESS, CLOSED |
| ResolutionCode | VARCHAR(30) | Resultado |

## Table: DisputeDocuments

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| DisputeDocumentId | UUID | PK |
| DisputeId | UUID | FK a Disputes |
| DocumentType | VARCHAR(30) | FORM, RECEIPT, ID |
| FileName | VARCHAR(255) | Nombre archivo |
| StoragePath | VARCHAR(500) | Ruta |
| UploadedAt | TIMESTAMP | Fecha carga |

## Table: Chargebacks

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| ChargebackId | UUID | PK |
| DisputeId | UUID | FK a Disputes |
| ExternalReference | VARCHAR(100) | Ref red |
| NetworkCode | VARCHAR(20) | VISA / MC |
| ChargebackStage | VARCHAR(30) | FIRST, SECOND, ARBITRATION |
| Amount | DECIMAL(18,2) | Monto |
| CurrencyCode | VARCHAR(10) | Moneda |
| DueDate | DATE | Fecha límite |
| Status | VARCHAR(20) | OPEN, SENT, WON, LOST |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: ChargebackEvents

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| ChargebackEventId | UUID | PK |
| ChargebackId | UUID | FK a Chargebacks |
| EventType | VARCHAR(30) | CREATED, SENT, RESPONDED, CLOSED |
| EventDate | TIMESTAMP | Fecha evento |
| Notes | VARCHAR(500) | Notas |
| CreatedBy | VARCHAR(100) | Usuario |

---

# 3.12. Contabilidad

## Table: LedgerAccounts

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| LedgerAccountId | UUID | PK |
| AccountCode | VARCHAR(30) | Código contable |
| AccountName | VARCHAR(150) | Nombre cuenta |
| AccountType | VARCHAR(30) | ASSET, LIABILITY, INCOME, EXPENSE |
| CurrencyCode | VARCHAR(10) | Moneda |
| Status | VARCHAR(20) | ACTIVE, INACTIVE |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: JournalEntries

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| JournalEntryId | UUID | PK |
| BusinessDate | DATE | Fecha negocio |
| SourceModule | VARCHAR(30) | AUTH, PAYMENT, SETTLEMENT, STATEMENT |
| SourceReference | VARCHAR(100) | Referencia origen |
| Description | VARCHAR(250) | Descripción |
| Status | VARCHAR(20) | PENDING, POSTED |
| CreatedAt | TIMESTAMP | Fecha creación |
| PostedAt | TIMESTAMP | Fecha contabilización |

## Table: JournalEntryLines

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| JournalEntryLineId | UUID | PK |
| JournalEntryId | UUID | FK a JournalEntries |
| LedgerAccountId | UUID | FK a LedgerAccounts |
| DebitAmount | DECIMAL(18,2) | Débito |
| CreditAmount | DECIMAL(18,2) | Crédito |
| CurrencyCode | VARCHAR(10) | Moneda |
| Description | VARCHAR(250) | Descripción |

## Table: AccountingMappings

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| AccountingMappingId | UUID | PK |
| EventType | VARCHAR(30) | PURCHASE_POSTED, PAYMENT_APPLIED, etc. |
| ProductCode | VARCHAR(30) | Producto |
| DebitAccountCode | VARCHAR(30) | Cuenta débito |
| CreditAccountCode | VARCHAR(30) | Cuenta crédito |
| EffectiveDate | DATE | Vigencia desde |
| EndDate | DATE | Vigencia hasta |

---

# 3.13. Notificaciones

## Table: NotificationPreferences

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| NotificationPreferenceId | UUID | PK |
| CustomerId | UUID | FK a Customers |
| EventType | VARCHAR(30) | AUTH_APPROVED, PAYMENT_RECEIVED |
| ChannelType | VARCHAR(20) | SMS, EMAIL, PUSH |
| IsEnabled | BOOLEAN | Activo |
| UpdatedAt | TIMESTAMP | Fecha actualización |

## Table: Notifications

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| NotificationId | UUID | PK |
| CustomerId | UUID | FK a Customers |
| CardId | UUID | FK a Cards |
| EventType | VARCHAR(30) | Evento |
| ChannelType | VARCHAR(20) | Canal |
| Destination | VARCHAR(150) | Destino |
| MessageBody | TEXT | Mensaje |
| Status | VARCHAR(20) | PENDING, SENT, FAILED |
| CreatedAt | TIMESTAMP | Fecha creación |
| SentAt | TIMESTAMP | Fecha envío |

## Table: NotificationTemplates

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| NotificationTemplateId | UUID | PK |
| EventType | VARCHAR(30) | Evento |
| ChannelType | VARCHAR(20) | Canal |
| LanguageCode | VARCHAR(10) | Idioma |
| SubjectTemplate | VARCHAR(250) | Asunto |
| BodyTemplate | TEXT | Plantilla |
| Status | VARCHAR(20) | ACTIVE, INACTIVE |
| UpdatedAt | TIMESTAMP | Fecha actualización |

---

# 3.14. Auditoría y seguridad

## Table: AuditLogs

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| AuditLogId | UUID | PK |
| EntityName | VARCHAR(100) | Tabla o entidad |
| EntityId | UUID | Id entidad |
| ActionType | VARCHAR(30) | INSERT, UPDATE, DELETE, BLOCK |
| PerformedBy | VARCHAR(100) | Usuario o sistema |
| Channel | VARCHAR(30) | WEB, API, BATCH |
| OldValue | TEXT | Valor anterior |
| NewValue | TEXT | Valor nuevo |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: ApiConsumers

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| ApiConsumerId | UUID | PK |
| ConsumerName | VARCHAR(100) | Integrador |
| ClientId | VARCHAR(100) | Client id |
| Status | VARCHAR(20) | ACTIVE, INACTIVE |
| CreatedAt | TIMESTAMP | Fecha creación |

## Table: ApiAccessLogs

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| ApiAccessLogId | UUID | PK |
| ApiConsumerId | UUID | FK a ApiConsumers |
| Endpoint | VARCHAR(250) | Endpoint |
| HttpMethod | VARCHAR(10) | GET, POST |
| RequestDate | TIMESTAMP | Fecha request |
| ResponseCode | INTEGER | Código respuesta |
| DurationMs | INTEGER | Duración |

## Table: UserRoles

| Campo | Tipo sugerido | Descripción |
|---|---|---|
| UserRoleId | UUID | PK |
| UserId | VARCHAR(100) | Usuario |
| RoleCode | VARCHAR(30) | ADMIN, OPERATIONS, FRAUD_ANALYST |
| RoleName | VARCHAR(100) | Nombre rol |
| AssignedAt | TIMESTAMP | Fecha asignación |

---

# 4. Relaciones principales

## Relación de alto nivel

```text
Customers 1 --- N Accounts
Customers 1 --- N Cards
Customers 1 --- N Disputes
Customers 1 --- N FraudCases

Accounts 1 --- N Cards
Accounts 1 --- N Authorizations
Accounts 1 --- N Transactions

CardProducts 1 --- N Cards
CardProducts 1 --- N CardProductFees
CardProducts 1 --- N CardProductRates

Cards 1 --- N CardPlastics
Cards 1 --- N CardStatusHistory
Cards 1 --- N CardLimits
Cards 1 --- N CardTokens
Cards 1 --- N Authorizations
Cards 1 --- N Transactions
Cards 1 --- N FraudAlerts
Cards 1 --- N Disputes

Authorizations 1 --- N AuthorizationRulesAudit
Authorizations 1 --- N AuthorizationReversals
Authorizations 1 --- 1 Transactions (en muchos casos)

Transactions 1 --- N TransactionEntries
Transactions 1 --- N TransactionCommissions
Transactions 1 --- N TransactionAdjustments
Transactions 1 --- N StatementLines
Transactions 1 --- N ClearingTransactions

CreditCardAccounts 1 --- N Statements
CreditCardAccounts 1 --- N Payments
CreditCardAccounts 1 --- N InterestAccruals

Payments 1 --- N PaymentAllocations
Payments 1 --- N PaymentReversals

Disputes 1 --- N DisputeDocuments
Disputes 1 --- N Chargebacks
Chargebacks 1 --- N ChargebackEvents

JournalEntries 1 --- N JournalEntryLines
LedgerAccounts 1 --- N JournalEntryLines