# Sprints - CardSwitchPlatform

> **Última auditoría:** 2026-05-25 (rebaseline verificado contra repositorio actual)
>
> Este documento refleja el estado REAL de cada sprint verificado contra el código fuente.
> Los estados se basan en evidencia técnica, no en intención.
>
> **Leyenda de estados:**
>
> - ✅ Completado — Código existe y funciona end-to-end (frontend + backend integrados)
> - 🟡 Parcial — Código existe pero con gaps funcionales o deuda técnica significativa
> - ⛔ Scaffold — Solo existe estructura backend sin frontend o sin integración real
> - 🔴 No iniciado — No existe código

---

## Sprint 1: Fundación, Autenticación y Autorización — ✅ Completado

**Objetivo:** Establecer la base del portal de administración, permitir el acceso seguro al sistema y el control de accesos.

### Frontend — ✅ Completado

- [x] Inicializar proyecto frontend (Angular 17).
- [x] Configurar routing, gestión de estado (Signals) y cliente HTTP apuntando a los microservicios actuales.
- [x] Implementar diseño UI/UX base (tema, componentes compartidos como botones, modales, tablas).
- [x] Integrar autenticación contra `CardVault.Api` (Login con JWT).
- [x] Desarrollar pantalla de Login y manejo de sesión (token refresh, logout).
- [x] Crear pantalla para gestión y creación de usuarios.
- [x] Crear pantalla para gestión de roles del sistema.
- [x] Implementar asignación dinámica de permisos, menús y acciones por roles.
- [x] Crear Layout principal (Sidebar, Header con información de usuario) que renderice los menús dinámicamente según el rol.
- [x] Implementar `authGuard` y `roleGuard` en rutas protegidas.
- [x] Implementar JWT interceptor (`auth.interceptor.ts`).

### Backend — ✅ Completado

- [x] Endpoint de login (`AuthController`) con JWT, refresh, MFA.
- [x] Todos los controllers de negocio protegidos con `[Authorize(Policy = "...")]` a nivel de clase.
- [x] Políticas de autorización granulares configuradas en `Program.cs` (25 policies).
- [x] Roles base sembrados: Admin, Operator, Auditor.
- [x] Usuarios semilla: admin, operator, auditor, admin.auditor, breakglass.
- [x] `AuditController` y `OutboxController` protegidos (fix 2026-03-26).
- [x] `AccountingController` y `EcommerceThreeDsController` con `[Authorize]` a nivel de clase (fix 2026-03-26).

---

## Sprint 2: Gestión de Clientes y Cuentas — ✅ Completado

**Objetivo:** Permitir la búsqueda, visualización y creación de clientes y sus respectivas cuentas.

- [x] Desarrollar pantalla de búsqueda/listado de clientes (`customer-list.component.ts`) integrada con API.
- [x] Implementar vista de detalle 360 del Cliente (`customer-detail.component.ts`).
- [x] Crear formulario para alta/modificación de clientes.
- [x] Incorporar a la vista 360 el listado de Cuentas asociadas al cliente.
- [x] Crear formulario para apertura de nuevas cuentas.
- [x] Pantalla de listado global de cuentas (`account-list.component.ts`).
- [x] Backend: `IssuerController`, `CustomerService`, `IssuerService` implementados.

### Nota de verificación

- [x] `customer.service.ts` consume API real y ya no tiene fallback silencioso a mocks.
- [x] `IssuerController` está protegido con `[Authorize(Policy = "CanOperateIssuer")]`.

---

## Sprint 3: Emisión y Gestión de Tarjetas — ✅ Completado

**Objetivo:** Administrar el ciclo de vida de las tarjetas de los clientes.

- [x] Pantalla para listar tarjetas asociadas a una cuenta (`card-list.component.ts`).
- [x] Formulario para emitir una nueva tarjeta (integrando la tokenización transparente del PAN).
- [x] Acciones de tarjeta: Bloqueo, Desbloqueo, Cancelación, Reposición.
- [x] Visualización de información de la tarjeta (sin exponer PAN completo, aprovechando la bóveda de tokens).
- [x] Backend: `IssuerController` con endpoints de cards, `TokensController` para vault.
- [x] Frontend: `card.service.ts` implementado.



---

## Sprint 4: Facturación, Ledger y Estados de Cuenta — ✅ Completado

**Objetivo:** Visualizar los movimientos financieros y la facturación de las cuentas.

- [x] Pantalla para consultar los movimientos (Ledger Entries) de una cuenta (`ledger-list.component.ts`).
- [x] Vista de Estados de Cuenta generados (`billing-statement.component.ts`).
- [x] Simulación/procesamiento de pagos a cuentas desde el frontend.
- [x] Backend: `LedgerController`, `BillingController`, `LedgerService`, `BillingService`.
- [x] Frontend: `finance.service.ts` implementado.



---

## Sprint 5: Auditoría, Switch Dashboard y Simulador — ✅ Completado (con deuda de seguridad)

**Objetivo:** Visualizar la operativa técnica, auditorías y ruteo transaccional del Switch.

- [x] Pantalla de Auditoría General (`audit-list.component.ts`) — integrada con IsoSwitch API.
- [x] Simulador Transaccional (`simulator.component.ts`) — interfaz para emitir mensajes ISO 8583.
- [x] Integrar visualización del ruteo y Monitor Transaccional del Switch, con detalle de trace, MTI y respuestas.
- [x] Pantalla de gestión de rutas BIN (`routing.component.ts`) y configuración de Catalogs (`catalogs.component.ts`).
- [x] Backend: IsoSwitch API (`Program.cs`) con endpoints de transactions, audit, catalogs, routing.
- [x] Frontend: `switch.service.ts`, `catalog.service.ts` implementados.

### Deuda técnica

- ~~**⚠️ Los endpoints de `IsoSwitch.Api` siguen públicos.**~~ **✅ Resuelto** — `harden-isoswitch-access` archivado el 2026-05-25. `IsoSwitch.Api` registra `AddAuthentication` / `AddAuthorization` y aplica `RequireAuthorization()` en todas las rutas operativas. 16 tests de auth-boundary en `IsoSwitch.Tests/Auth/AuthBoundaryTests.cs` validan el comportamiento. Suite total: 165 tests / 0 fallos.


---

## Sprint 6: Operaciones, Liquidación y Disputas — ✅ Completado

**Objetivo:** Consolidar la operativa contable de cierre, gestión de reclamos y administración global.

- [x] **Compensación y Liquidación:** Pantalla de settlements (`settlement-list.component.ts`), `settlement.service.ts`.
- [x] **Disputas y Reclamos:** Gestión de ciclo de vida (`dispute-list.component.ts`), `dispute.service.ts`.
- [x] **Ciclo de Vida Extendido:** Acciones de bloqueo/desbloqueo con registro de motivos e historial.
- [x] **Listado Global de Cuentas:** Vista maestra de cuentas (`account-list.component.ts`).
- [x] **Olvido de Contraseña:** Flujo de recuperación (`forgot-password.component.ts`).
- [x] Backend: `SettlementController`, `DisputesController`, `SettlementService`, `DisputeService`.



---

## Sprint 7: Motor de Crédito, Facturación y Diferidos — ✅ Completado

**Objetivo:** Implementar la lógica financiera avanzada de tarjetas de crédito.

- [x] **Facturación y Estados de Cuenta (v62):** Backend con `StatementPdfService`, `DailyInterestAccrualService`, `MinimumPaymentService`.
- [x] **Compras en Cuotas y Diferidos (v66):** `installment-list.component.ts`, `installment.service.ts`, `InstallmentService`.
- [x] **Registro de Pagos (v63):** `PaymentAllocatorService`, procesamiento desde frontend.
- [x] **Comisiones y Cargos (v58):** `FeeService`, `BillingMaintenanceService`.



---

## Sprint 8: Seguridad Proactiva y Tokenización — 🟡 Parcial

**Objetivo:** Fortalecer el motor de autorización y la protección de datos.

- [x] **Motor Antifraude (v60):** `antifraud-list.component.ts`, `antifraud.service.ts`, `RiskController`, `AntifraudController`, `RiskDecisionService`.
- [x] **Validación de PIN y Seguridad PCI (v58-v2):** `PinService` con hashing y bloqueos.
- [x] **Motor de Autorización & Reversos (v56, v57):** `HoldService`, `ReversalWorker`, `TransactionStateMachine` en IsoSwitch.
- [x] **Tokenización (v68):** `vault.component.ts`, `vault.service.ts`, `TokensController` con gestión de bóveda.
- [x] **Pagos E-commerce Seguros (v70):** `ecommerce-monitor.component.ts`, `EcommerceThreeDsController`, `ThreeDsService`.

### Nota

- Existen tests backend para `RiskDecisionService`, `HoldService` y handlers ISO críticos, por lo que ya no corresponde afirmar que no hay validación automatizada del sprint.
- La cobertura dedicada del flujo `3DS` (`EcommerceThreeDsController` / `ThreeDsService`) sigue faltando, así que `v70` permanece parcialmente verificable.
- **PCI-DSS Compliance:** `fix-vault-rotation-policy` archivado 2026-05-30 — registró `vault_admin_ops` rate-limit policy, agregó auditoría transaccional de rotación de claves, y 288 tests green. Cierra brecha normativa PCI-DSS 3.6.4 y Superintendencia de Bancos del Ecuador Resolución JB-2014-3066.

---

## Sprint 9: Ecosistema, Conectividad e Inteligencia — ✅ COMPLETADO

**Objetivo declarado:** Expandir la plataforma hacia canales digitales y análisis de datos.

**Estado real:** Implementación completa end-to-end. Los 7 servicios frontend y sus componentes correspondientes están funcionales, integrados en el router y accesibles desde el sidebar.

### Backend & Frontend Integration

| Feature | Backend Controller | Angular Service | Angular Component | Estado |
|---------|-------------------|-----------------|-------------------|--------|
| Fidelización (v67) | `LoyaltyController` | `LoyaltyService` | `LoyaltyListComponent` | ✅ |
| Wallets Digitales (v69) | `WalletsController` | `WalletsService` | `WalletsListComponent` | ✅ |
| Open Banking (v73) | `OpenBankingController` | `OpenBankingService` | `OpenBankingListComponent` | ✅ |
| Notificaciones (v74) | `NotificationsController` | `NotificationsService` | `NotificationsListComponent` | ✅ |
| Analytics/BI (v75) | `AnalyticsController` | `AnalyticsService` | `AnalyticsDashboardComponent` | ✅ |
| Gestión de Cupos (v71) | `CreditLimitManagementController` | `CreditLimitService` | `CreditLimitListComponent` | ✅ |
| Integración Contable (v65) | `AccountingController` | `AccountingService` | `AccountingListComponent` | ✅ |

### Rutas e Integración UI — ✅ Completado

- [x] Pantalla de Fidelización y Cashback/Puntos.
- [x] Pantalla de integración con Billeteras Digitales.
- [x] Pantalla de APIs Open Banking.
- [x] Pantalla de motor de Notificaciones push/SMS.
- [x] Tableros de BI/Analytics.
- [x] Pantalla de Gestión de Cupos.
- [x] Pantalla de Integración Contable.
- [x] Registro en `app.routes.ts`.
- [x] Integración en `sidebar.component.ts`.

---

## Resumen de Estado Real

| Sprint | Declarado Antes | Estado Real | Bloqueante |
|--------|----------------|-------------|------------|
| Sprint 1 | ✅ Completado | ✅ **Completado** — Todos los controllers protegidos con policies | No |
| Sprint 2 | ✅ Completado | ✅ **Completado** — customers/accounts operan contra API real y `IssuerController` está protegido | No |
| Sprint 3 | ✅ Completado | ✅ Completado | No |
| Sprint 4 | ✅ Completado | ✅ Completado | No |
| Sprint 5 | ✅ Completado | ✅ Completado — deuda de seguridad IsoSwitch resuelta en `harden-isoswitch-access` (2026-05-25) | No |
| Sprint 6 | ✅ Completado | ✅ Completado | No |
| Sprint 7 | ✅ Completado | ✅ Completado | No |
| Sprint 8 | ✅ Completado | 🟡 **Parcial** — falta cobertura automatizada específica para 3DS ecommerce | No |
| Sprint 9 | ✅ Completado | ✅ **COMPLETADO** | No |

## Deuda Transversal

| Deuda | Impacto | Fase del Backlog |
|-------|---------|-----------------|
| Endpoints operativos de `IsoSwitch.Api` sin autenticación/autorización | ✅ Resuelto — `harden-isoswitch-access` archivado 2026-05-25; 16 tests de auth-boundary en `IsoSwitch.Tests` | Fase 1 |
| Cobertura mínima incompleta en tests | ✅ Resuelto — 184 tests / 0 fallos (CardVault 147 + IsoSwitch 37); cobertura 3DS ecommerce completa | Fase 5 |
| Flujo 3DS sin tests automáticos dedicados | ✅ Resuelto — `add-3ds-ecommerce-tests`: 13 auth-boundary + 6 handler tests (2026-05-25) | Fase 5 |

---

## Sprint 10: Cobranzas y Operaciones Avanzadas — 🟡 En Progreso

**Objetivo:** Desarrollar los módulos avanzados de recuperación de cartera y gestión operativa. Todos los desarrollos de este sprint usan metodología SDD (Spec-Driven Development) y Strict TDD.

| Feature | Backend | Frontend | Estado |
|---------|---------|----------|--------|
| Mora Temprana (v76) | ✅ CQRS + Worker | ✅ Completo | ✅ Completado (`v76-mora-temprana` archivado 2026-05-25) |
| Gestión de Cobranzas y Agencias (v77) | 🔴 | 🔴 | 🔴 No iniciado |
| Reestructuración y Refinanciamiento (v78) | 🔴 | 🔴 | 🔴 No iniciado |
| Condonaciones y Castigos (v79) | 🔴 | 🔴 | 🔴 No iniciado |
