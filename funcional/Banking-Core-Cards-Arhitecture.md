# Core Bancario de Tarjetas

## Arquitectura y Modelo de Dominio

Sistema Core Bancario para gestión de **Tarjetas de Crédito y Débito** incluyendo:

* Switch transaccional
* Motor de autorización
* Gestión de cuentas
* Facturación
* Pagos
* Compensación
* Antifraude
* Reclamos
* Eventos del sistema

---

# 1. Modelo de Dominio del Core de Tarjetas

El modelo de dominio representa las **entidades principales del negocio bancario de tarjetas** y sus relaciones.

---

# 2. Entidad Cliente (Customer)

Representa al titular de la tarjeta.

## Campos

* CustomerId
* TipoCliente
* Identificacion
* Nombre
* Direccion
* Telefono
* Email
* FechaAlta
* EstadoCliente
* ScoreCrediticio

## Relaciones

Cliente puede tener:

* múltiples cuentas
* múltiples tarjetas
* reclamos
* historial de transacciones

```
Cliente
 ├── Cuentas
 ├── Tarjetas
 └── Reclamos
```

---

# 3. Entidad Cuenta (Account)

Representa una cuenta bancaria asociada a la tarjeta.

## Tipos de cuenta

* Cuenta corriente
* Cuenta de ahorros
* Cuenta de tarjeta de crédito

## Campos

* AccountId
* CustomerId
* TipoCuenta
* SaldoDisponible
* SaldoContable
* Moneda
* EstadoCuenta
* FechaApertura

## Relaciones

```
Cuenta
 ├── Tarjetas
 ├── Transacciones
 ├── Pagos
 └── MovimientosContables
```

---

# 4. Entidad Tarjeta (Card)

Representa el plástico o tarjeta digital.

## Campos

* CardId
* AccountId
* CardNumber (PAN)
* Token
* TipoTarjeta
* Marca
* EstadoTarjeta
* FechaEmision
* FechaExpiracion
* CVV
* LimiteCredito
* CupoDisponible

## Estados posibles

* Creada
* Activa
* Bloqueada
* Vencida
* Cancelada
* Reemplazada

---

# 5. Entidad Autorización (Authorization)

Solicitud enviada al switch para validar una transacción.

## Campos

* AuthorizationId
* CardId
* Monto
* Moneda
* Comercio
* Terminal
* FechaHora
* Canal
* Resultado
* CodigoRespuesta

## Flujo

```
POS / ATM / Ecommerce
        ↓
Switch Transaccional
        ↓
Motor de Autorización
        ↓
Respuesta
```

---

# 6. Entidad Transacción (Transaction)

Movimiento financiero generado por una autorización.

## Campos

* TransactionId
* AuthorizationId
* CardId
* AccountId
* Monto
* TipoTransaccion
* Estado
* FechaTransaccion
* Comercio
* Ciudad
* Pais

## Tipos de transacción

* Compra
* Retiro ATM
* Avance de efectivo
* Pago
* Reverso
* Comisión
* Interés

---

# 7. Entidad Compensación (Clearing)

Proceso de liquidación entre entidades financieras.

## Campos

* ClearingId
* TransactionId
* FechaCompensacion
* RedPago
* MontoCompensado
* Estado

---

# 8. Entidad Pago (Payment)

Pago aplicado a una tarjeta de crédito.

## Campos

* PaymentId
* AccountId
* CardId
* Monto
* FechaPago
* CanalPago
* EstadoPago

## Canales

* Caja
* Transferencia
* Débito automático
* Corresponsales
* App móvil

---

# 9. Entidad Estado de Cuenta (Statement)

Resumen mensual de consumos y saldos.

## Campos

* StatementId
* CardId
* FechaCorte
* FechaPago
* PagoMinimo
* PagoTotal
* SaldoAnterior
* SaldoActual

---

# 10. Entidad Fraude (FraudCase)

Caso de fraude detectado por el sistema.

## Campos

* FraudCaseId
* CardId
* TransactionId
* NivelRiesgo
* EstadoCaso
* FechaDeteccion
* AnalistaAsignado

---

# 11. Entidad Reclamo (Dispute)

Gestión de disputas o contracargos.

## Campos

* DisputeId
* TransactionId
* CardId
* Motivo
* EstadoCaso
* FechaRegistro
* Resolucion

---

# 12. Relación General del Dominio

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
                   │                 │
                   │                 └── Compensación
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

# 13. Microservicios del Core de Tarjetas

| Microservicio         | Responsabilidad                 |
| --------------------- | ------------------------------- |
| Customer Service      | gestión de clientes             |
| Account Service       | cuentas bancarias               |
| Card Service          | administración de tarjetas      |
| Authorization Service | autorización de transacciones   |
| Transaction Service   | registro de transacciones       |
| Fraud Service         | detección de fraude             |
| Statement Service     | generación de estados de cuenta |
| Payment Service       | procesamiento de pagos          |
| Dispute Service       | gestión de reclamos             |
| Settlement Service    | compensación                    |

---

# 14. Eventos del Sistema

* CardCreated
* CardActivated
* TransactionAuthorized
* TransactionRejected
* TransactionReversed
* PaymentApplied
* StatementGenerated
* FraudDetected
* CardBlocked

---

# 15. Flujo de Compra con Tarjeta

```
Cliente paga en POS
       ↓
Switch recibe transacción
       ↓
Motor de autorización valida

- estado de tarjeta
- cupo disponible
- reglas antifraude

       ↓
Autorización aprobada
       ↓
Se registra transacción
       ↓
Proceso de compensación
       ↓
Movimiento aparece en estado de cuenta
```

---

# 16. Componentes de Arquitectura

```
POS / ATM / Ecommerce
        │
        ▼
Switch Transaccional
        │
        ▼
API Gateway
        │
        ▼
Microservicios

- Customer Service
- Account Service
- Card Service
- Authorization Service
- Transaction Service
- Fraud Service
- Payment Service
- Statement Service
- Dispute Service
- Settlement Service

        │
        ▼
Event Bus (Kafka)

        │
        ▼
Base de datos
```

---

# 17. Tecnologías sugeridas

Backend

* .NET
* Java
* Node.js

Mensajería

* Kafka
* RabbitMQ

Base de datos

* PostgreSQL
* Oracle
* SQL Server

Seguridad

* OAuth2
* JWT
* Tokenización

---

# 18. Capacidades del Core Bancario

El sistema permite

* gestión de clientes
* gestión de cuentas
* emisión de tarjetas
* autorización de transacciones
* retiros ATM
* pagos
* facturación
* compensación
* detección de fraude
* reclamos y contracargos
* generación de estados de cuenta
* analítica del negocio de tarjetas

---

# 19. Roadmap

v56 – Motor de autorización
v57 – Reversos
v58 – ATM y avances
v59 – Compensación
v60 – Antifraude
v61 – Ciclo de vida de tarjetas
v62 – Facturación
v63 – Pagos
v64 – Reclamos
v65 – Integración contable
v66 – Cuotas
v67 – Cashback
v68 – Tokenización
v69 – Wallets
v70 – 3D Secure
v71 – Sobregiros
v72 – Campañas
v73 – Open Banking
v74 – Notificaciones
v75 – Analytics

---

# Fin del documento
