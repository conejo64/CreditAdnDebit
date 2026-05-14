# Core Bancario de Tarjetas

## Arquitectura, Modelo de Datos, Switch e Infraestructura

Proyecto conceptual de **Core Bancario para Tarjetas de Crédito y Débito** con enfoque de arquitectura moderna basada en microservicios.

Incluye:

* Arquitectura del sistema
* Modelo de datos
* Switch transaccional
* Motor antifraude
* Eventos del sistema
* Roadmap evolutivo

---

# 1. Arquitectura del Core Bancario

## Canales de entrada

Los sistemas externos que consumen el Core de tarjetas son:

* POS
* ATM
* E-commerce
* Mobile Banking
* Web Banking
* Open Banking APIs
* Call Center

---

## Arquitectura general

```
POS / ATM / E-commerce / Apps
           │
           ▼
    Switch Transaccional
           │
           ▼
       API Gateway
           │
           ▼
      Microservicios

    Customer Service
    Account Service
    Card Service
    Authorization Service
    Transaction Service
    Fraud Service
    Payment Service
    Statement Service
    Dispute Service
    Settlement Service
    Ledger Service

           │
           ▼
        Event Bus
        (Kafka)

           │
           ▼
     Base de Datos
```

---

# 2. Microservicios del sistema

## Customer Service

Responsable de:

* gestión de clientes
* perfil del cliente
* segmentación
* datos personales

---

## Account Service

Responsable de:

* cuentas bancarias
* saldos
* bloqueos de fondos
* límites

---

## Card Service

Responsable de:

* emisión de tarjetas
* activación
* bloqueo
* renovación
* reposición
* tokenización

---

## Authorization Service

Responsable de:

* validar transacciones
* validar saldo o cupo
* validar reglas antifraude
* responder al switch

---

## Transaction Service

Responsable de:

* registrar transacciones
* registrar movimientos
* registrar comisiones

---

## Fraud Service

Responsable de:

* análisis antifraude
* reglas de riesgo
* alertas
* bloqueo preventivo

---

## Payment Service

Responsable de:

* registrar pagos
* aplicar pagos
* reversar pagos

---

## Statement Service

Responsable de:

* estados de cuenta
* intereses
* facturación

---

## Dispute Service

Responsable de:

* reclamos
* disputas
* contracargos

---

## Settlement Service

Responsable de:

* compensación
* conciliación
* liquidación

---

## Ledger Service

Responsable de:

* contabilidad
* asientos contables
* integración contable

---

# 3. Modelo de Datos

## Entidades principales

Cliente
Cuenta
Tarjeta
Autorización
Transacción
Compensación
Estado de cuenta
Pago
Fraude
Reclamo

---

## Relación del dominio

```
Cliente
   │
   └── Cuenta
           │
           └── Tarjeta
                   │
                   ├── Autorización
                   │        │
                   │        └── Transacción
                   │                │
                   │                └── Compensación
                   │
                   ├── Estado de Cuenta
                   │
                   ├── Pagos
                   │
                   ├── Fraude
                   │
                   └── Reclamos
```

---

# 4. Switch Transaccional

El switch es responsable de:

* recibir transacciones
* enrutar solicitudes
* transformar mensajes
* comunicarse con el Core

---

## Flujo de autorización

```
1 POS envía solicitud
2 Switch recibe mensaje
3 Se valida tarjeta
4 Se valida cupo o saldo
5 Se evalúan reglas antifraude
6 Se responde aprobado o rechazado
7 Se registra transacción
```

---

# 5. ISO8583 (Referencia)

Mensajes más comunes:

```
0100 Autorización request
0110 Autorización response
0200 Financial request
0210 Financial response
0400 Reversal request
0410 Reversal response
```

---

# 6. Motor Antifraude

## Objetivo

Detectar patrones sospechosos en transacciones.

---

## Reglas comunes

* múltiples transacciones en pocos minutos
* transacción en país diferente
* intentos incorrectos de PIN
* compras fuera del patrón del cliente
* transacciones de alto monto

---

## Acciones del sistema

* generar alerta
* bloquear tarjeta
* requerir autenticación adicional
* abrir caso de fraude

---

# 7. Eventos del sistema (Event Driven)

Eventos publicados en Kafka:

```
CustomerCreated
AccountCreated
CardCreated
CardActivated
TransactionAuthorized
TransactionRejected
TransactionReversed
PaymentApplied
FraudDetected
DisputeOpened
StatementGenerated
```

---

# 8. Tecnologías sugeridas

## Backend

* .NET
* Java
* Node.js

---

## Mensajería

* Kafka
* RabbitMQ

---

## Base de datos

* PostgreSQL
* SQL Server
* Oracle

---

## Seguridad

* OAuth2
* JWT
* Tokenización

---

# 9. Roadmap del Core Bancario

## v56 – v65

* autorización
* reversos
* ATM
* compensación
* fraude
* ciclo de vida de tarjeta
* facturación
* pagos
* reclamos
* contabilidad

---

## v66 – v75

* cuotas
* cashback
* loyalty
* tokenización
* wallets
* 3D secure
* open banking
* notificaciones
* analytics

---

## v76 – v85

* cobranzas
* promesas de pago
* refinanciación
* gestión de mora

---

## v86 – v95

* multi moneda
* alta disponibilidad
* observabilidad
* analytics avanzado

---

## v96 – v100

* machine learning antifraude
* data lake
* pricing dinámico
* gobierno de datos

---

# Fin del documento
