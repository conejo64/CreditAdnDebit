# Backlog Maestro - CardSwitchPlatform

## Objetivo

Este documento consolida dos cosas:

1. El plan de estabilizacion obligatorio antes de seguir agregando features.
2. El backlog funcional de evolucion del core bancario de tarjetas.

## Regla de ejecucion

Antes de comprometer nuevas funcionalidades de negocio en `Sprints.md`, se deben cerrar los bloqueos reales de seguridad y calidad verificados contra el codigo actual.

Estado recomendado actual:

- Nuevas features: `v77+ en espera (ver Fase 6)`
- Hardening tecnico: `Fase 1 cerrada · Fase 5 cerrada`
- Foco P1 resuelto: `IsoSwitch.Api protegido · 184 tests / 0 fallos · 3DS ecommerce completo`
- Roadmap funcional v76: `Completado y archivado (2026-05-25)`
- Roadmap funcional v77+: `Planificado, no comprometido`

---

## Plan de Estabilizacion Previo al Roadmap

## Fase 1 - Base bancaria obligatoria

**Prioridad:** P1  
**Objetivo:** cerrar autenticacion y autorizacion real en frontend y backend.

**Estado verificado:** `Cerrada` _(rebaseline 2026-05-25 — `harden-isoswitch-access` archivado; 16 tests de auth-boundary confirman 401/403 en IsoSwitch.Api)_

### Alcance

- Mantener alineado el contrato de login, refresh y sesion actual entre Angular y `CardVault.Api`.
- Mantener `auth interceptor`, guards y refresh token sobre sesion real en frontend.
- Verificar proteccion real de endpoints operativos en ambos servicios.
- Cerrar exposicion publica de `IsoSwitch.Api` en rutas de transacciones, monitor, routing, catalogs y auditoria.
- Revisar consistencia de roles y permisos entre backend y menus protegidos del frontend.

### Entregables

- Login real con `access token` y `refresh token`.
- Guard de frontend basado en sesion real.
- Endpoints sensibles protegidos en `CardVault.Api` e `IsoSwitch.Api`.
- Matriz minima de roles:
  - `Admin`
  - `Operator`
  - `Auditor`

### Definition of Done

- No existe acceso funcional a pantallas operativas sin token valido.
- No existe fallback silencioso a usuario mock en modulos protegidos.
- Los endpoints criticos de `CardVault.Api` e `IsoSwitch.Api` devuelven `401/403` correctamente.

---

## Fase 2 - Estabilizacion de contratos

**Prioridad:** P2  
**Objetivo:** corregir contratos HTTP y acoplamientos frontend-backend.

**Estado verificado:** `Mayormente cerrada`

### Alcance

- Mantener `openspec/specs/http-contracts/spec.md` como fuente unica de compatibilidad.
- Validar que cambios futuros de contrato actualicen frontend y backend en la misma iteracion.
- Revisar regresiones de URLs, payloads y nombres de propiedades cuando cambie un modulo critico.

### Entregables

- Inventario de contratos reales por modulo.
- Tabla de compatibilidad frontend/backend publicada.
- Regla de mantenimiento para cambios de contrato.

### Definition of Done

- Cada modulo critico usa endpoints reales.
- No quedan pantallas criticas dependiendo de mocks para operar.
- Los contratos quedan documentados como fuente unica y se mantienen al dia.

---

## Fase 3 - Sincerar arquitectura

**Prioridad:** P1  
**Objetivo:** ordenar el sistema segun su estado real y evitar sobre-diseno.

### Decision arquitectonica

Asumir por ahora esta estructura:

- `CardVault`: modular monolith de negocio emisor
- `IsoSwitch`: servicio separado de switching
- `IsoAudit`: servicio auxiliar de auditoria

### Alcance

- Definir modulos internos estables en `CardVault`.
- Reducir ambiguedad entre `Domain`, `Application`, `Infrastructure` y `Api`.
- Alinear documentos de arquitectura con el estado real del codigo.
- Dejar explicito que no se fragmentara a 10 microservicios todavia.

### Entregables

- Documento de arquitectura actualizada.
- Mapa de modulos reales y ownership funcional.
- Criterios para futuros splits por dominio.

### Definition of Done

- La arquitectura documentada coincide con la implementada.
- Los modulos tienen fronteras claras.
- No hay expectativas irreales en `backend_architecture.md`.

---

## Fase 4 - Extraer logica de Program.cs

**Prioridad:** P2  
**Objetivo:** sacar casos de uso, validaciones y flujos del host HTTP.

**Estado verificado:** `Mayormente cerrada`

### Alcance

- Mantener `IsoSwitch.Api/Program.cs` limitado a bootstrap, middleware y registro de dependencias.
- Evitar regresiones que vuelvan a incrustar logica de negocio en el host.
- Mantener separacion explicita entre endpoints productivos y demo/simulator.
- Mover el control de acceso del switch a la Fase 1, porque el gap vigente ya no es el tamano del archivo sino la exposicion publica.

### Entregables

- Endpoints organizados por modulo.
- Convencion clara de carpetas.
- `Program.cs` sin volver a concentrar casos de uso.

### Definition of Done

- `Program.cs` ya no contiene logica de negocio extensa.
- Los endpoints demo quedan claramente aislados.
- La mantenibilidad del switch mejora sin reabrir deuda de host masivo.

---

## Fase 5 - Calidad minima obligatoria

**Prioridad:** P1  
**Objetivo:** introducir pruebas minimas para evitar regresiones.

**Estado verificado:** `Cerrada` _(rebaseline 2026-05-25 — 184 tests / 0 fallos; 3DS auth-boundary + handler tests completos)_

### Alcance

- Expandir la base existente de tests de backend en `CardVault.Tests` e `IsoSwitch.Tests`.
- Cubrir flujos minimos:
  - auth/login/refresh
  - emision y bloqueo de tarjeta
  - statement/payment
  - auth ISO
  - reversal
  - ecommerce 3DS
- Definir smoke tests funcionales reales para frontend.

### Entregables

- Suites backend ampliadas con fixtures reutilizables.
- Smoke tests de frontend mas alla de `AppComponent`.
- Pipeline local de validacion.

### Definition of Done

- Los flujos criticos tienen cobertura automatizada minima.
- Los cambios de contrato rompen tests antes de llegar a sprint funcional.
- El frontend tiene validacion automatizada minima de login, rutas protegidas y pantallas criticas.

---

## Fase 6 - Roadmap funcional realista

**Prioridad:** P2  
**Objetivo:** retomar evolucion de negocio sobre una base estable.

### Orden recomendado de reactivacion

1. `v76` Gestion de mora temprana
2. `v77` Gestion de cobranza operativa
3. `v78` Promesas de pago
4. `v79` Segmentacion de cartera
5. `v80` Contactabilidad multicanal
6. `v81+` Refinanciamiento y reestructuracion

### Criterio de entrada

Ningun item de esta fase debe pasar a sprint mientras Fase 1 y Fase 5 sigan abiertas en sus entregables principales.

---

## Plan sugerido por semanas

## Semana 1 ✅ Completada (2026-05-25)

- ~~Cerrar autenticacion y autorizacion en `IsoSwitch.Api`.~~
- ~~Proteger rutas operativas y dejar separacion explicita entre productivo y demo.~~
- ~~Verificar `401/403` reales desde frontend y llamadas directas.~~

## Semana 2 ✅ Completada (2026-05-25)

- ✅ Cobertura backend: 184 tests / 0 fallos (CardVault 147 + IsoSwitch 37).
- ✅ Tests 3DS ecommerce: 13 auth-boundary + 6 handler tests en `EcommerceThreeDsEndpointAuthTests` y `EcommerceThreeDsHandlerTests`.
- ✅ Regresiones de auth, auth ISO y reversal cubiertas.

## Semana 3

- Incorporar smoke tests reales de frontend.
- Cubrir login, proteccion de rutas, carga de customers y consulta de statements.
- Definir comando local de validacion previo a merge.

## Semana 4

- Cerrar deuda residual de hardening.
- Revalidar `funcional/Sprints.md` y `funcional/BackLog.md` contra el resultado.
- Si Fase 1 y Fase 5 quedan cerradas, seleccionar el siguiente item funcional a activar desde `v76`.

---

## Plan operativo por equipo

Nota: este plan reemplaza al operativo historico y parte del estado real ya verificado en codigo.

## Backend

- Registrar autenticacion y autorizacion en `IsoSwitch.Api`.
- Aplicar proteccion a rutas operativas de transacciones, monitor, routing, catalogs y auditoria.
- Mantener demo/simulator explicitamente separado del flujo operativo.
- Expandir tests en `CardVault.Tests` e `IsoSwitch.Tests`.
- Agregar cobertura dedicada para 3DS ecommerce.

## Frontend

- Verificar UX real ante `401/403` de `IsoSwitch.Api`.
- Agregar smoke tests minimos de login, guardias y pantallas criticas.
- Mantener alineada la tabla de contratos en `openspec/specs/http-contracts/spec.md`.

## Datos e infraestructura

- Mantener fixtures reutilizables para pruebas backend.
- Documentar credenciales y usuarios semilla de desarrollo cuando cambien.
- Definir pipeline local de validacion antes de merge.

## Criterio de cierre actual

- Los endpoints operativos de `CardVault.Api` e `IsoSwitch.Api` rechazan acceso no autorizado.
- Los flujos backend criticos tienen cobertura automatizada minima.
- El frontend tiene smoke validation real mas alla de `AppComponent`.
- Solo entonces se reabre roadmap funcional nuevo.

---

## Matriz operativa P1/P2/P3

### P1 - Ejecutar primero

- ~~Cerrar autenticacion/autorizacion de `IsoSwitch.Api`.~~ ✅ Cerrado (2026-05-25)
- ~~Verificar `401/403` reales end-to-end.~~ ✅ Verificado con 16 auth-boundary tests
- ~~Completar cobertura minima backend.~~ ✅ 184 tests / 0 fallos (incluyendo 3DS completo)
- Agregar smoke tests reales de frontend — 🔲 Pendiente (Fase 5 cerrada en backend; frontend pendiente)

### P2 - Ejecutar inmediatamente despues

- Sincerar arquitectura y ownership.
- Mantener `Program.cs` del switch sin recaer en host masivo.
- Revalidar documentacion tecnica despues del hardening.

### P3 - Solo despues del hardening

- ~~`v76` Gestion de mora temprana.~~ ✅ Completado y archivado (2026-05-25)
- `v77` Gestion de cobranza operativa. _(desbloqueado cuando Fase 5 cierre)_
- `v78` Promesas de pago.
- `v79+` Evolucion de cartera y refinanciamiento.

---

## Tickets concretos de arranque

### Backend

- Ticket `BE-SEC-ISO-01`: agregar autenticacion y autorizacion a `IsoSwitch.Api`.
- Ticket `BE-SEC-ISO-02`: proteger rutas operativas y separar explicitamente demo/productivo.
- Ticket `BE-QA-01`: ampliar tests de `CardVault.Tests` e `IsoSwitch.Tests` para flujos criticos.
- Ticket `BE-QA-02`: agregar tests dedicados de 3DS ecommerce.

### Frontend

- Ticket `FE-QA-01`: agregar smoke tests de login y rutas protegidas.
- Ticket `FE-QA-02`: agregar smoke tests de customers y statements.
- Ticket `FE-SEC-01`: verificar manejo UX de `401/403` cuando `IsoSwitch.Api` quede protegido.

### Datos e infraestructura

- Ticket `DATA-QA-01`: definir fixtures reutilizables y checklist local de validacion.

### Criterio de cierre del arranque

- `IsoSwitch.Api` ya no expone operaciones criticas en publico.
- Los tests cubren auth, auth ISO, reversal y 3DS con minima confianza.
- El frontend tiene smoke tests utiles para detectar regresiones basicas.

---

## Matriz de prioridad

### P1 - Bloqueantes antes de nuevas features

- Fase 1 - Base bancaria obligatoria
- Fase 5 - Calidad minima obligatoria

### P2 - Orden estructural necesario

- Fase 2 - Estabilizacion de contratos
- Fase 3 - Sincerar arquitectura
- Fase 4 - Extraer logica de `Program.cs`
- Fase 6 - Roadmap funcional realista

### P3 - Nuevas capacidades de negocio

- Todo backlog funcional nuevo a partir de `v76`

---

## Backlog funcional de evolucion

Nota: los items siguientes permanecen en backlog, pero no deben entrar a sprint hasta cerrar las fases de estabilizacion anteriores.

---

## v76 - Gestion de mora temprana

### Epica

Gestion inicial de clientes con atraso en pagos de tarjeta de credito.

### [Backlog] Identificacion automatica de cuentas en mora

**Historia de Usuario**

Como sistema financiero  
Quiero identificar automaticamente las cuentas con pagos vencidos  
Para iniciar el proceso de cobranza temprana.

**Criterios de Aceptacion**

- Calcular dias de mora automaticamente
- Identificar cuentas con saldo vencido
- Marcar estado delinquent en la cuenta
- Generar registro en historial de mora
- Ejecutar proceso batch diario

**Prioridad:** Alta

### [Backlog] Clasificacion de mora por rango de dias

**Historia de Usuario**

Como sistema de riesgo  
Quiero clasificar la mora por rangos de dias  
Para priorizar estrategias de cobranza.

**Criterios de Aceptacion**

- Clasificar mora 1-30 dias
- Clasificar mora 31-60 dias
- Clasificar mora 61-90 dias
- Clasificar mora >90 dias
- Actualizar clasificacion diariamente

**Prioridad:** Alta

---

## v77 - Gestion de cobranza operativa

### Epica

Gestion de operaciones del equipo de cobranzas.

### [Backlog] Bandeja de trabajo de cobranzas

**Historia de Usuario**

Como analista de cobranzas  
Quiero visualizar las cuentas morosas asignadas  
Para gestionar el contacto con los clientes.

**Criterios de Aceptacion**

- Mostrar lista de cuentas asignadas
- Mostrar monto vencido
- Mostrar dias de mora
- Permitir ordenar por prioridad
- Permitir registrar gestion

**Prioridad:** Alta

### [Backlog] Registro de gestiones de cobranza

**Historia de Usuario**

Como gestor de cobranza  
Quiero registrar las gestiones realizadas  
Para tener trazabilidad del contacto con el cliente.

**Criterios de Aceptacion**

- Registrar tipo de contacto
- Registrar resultado de la gestion
- Registrar comentarios
- Registrar fecha y usuario

**Prioridad:** Alta

---

## v78 - Promesas de pago

### Epica

Gestion de compromisos de pago.

### [Backlog] Registro de promesas de pago

**Historia de Usuario**

Como gestor de cobranza  
Quiero registrar promesas de pago  
Para monitorear el cumplimiento del cliente.

**Criterios de Aceptacion**

- Registrar monto prometido
- Registrar fecha de pago prometida
- Registrar canal de contacto
- Cambiar estado de promesa automaticamente

**Prioridad:** Alta

### [Backlog] Monitoreo de cumplimiento de promesas

**Historia de Usuario**

Como sistema financiero  
Quiero verificar automaticamente el cumplimiento de promesas  
Para actualizar el estado de la gestion.

**Criterios de Aceptacion**

- Verificar pago contra promesa
- Marcar promesa como cumplida
- Marcar promesa como incumplida
- Generar alerta de incumplimiento

**Prioridad:** Alta

---

## v79 - Segmentacion de cartera

### Epica

Clasificacion estrategica de cartera morosa.

### [Backlog] Segmentacion automatica de cartera

**Historia de Usuario**

Como sistema de riesgo  
Quiero segmentar la cartera morosa  
Para aplicar estrategias de cobranza diferenciadas.

**Criterios de Aceptacion**

- Segmentar por dias de mora
- Segmentar por monto de deuda
- Segmentar por score del cliente
- Actualizar segmentacion diariamente

**Prioridad:** Media

---

## v80 - Contactabilidad multicanal

### Epica

Comunicacion automatica con clientes.

### [Backlog] Envio automatico de recordatorios de pago

**Historia de Usuario**

Como sistema  
Quiero enviar recordatorios de pago  
Para reducir la morosidad.

**Criterios de Aceptacion**

- Enviar SMS de recordatorio
- Enviar correo electronico
- Enviar notificacion push
- Registrar intento de contacto

**Prioridad:** Alta

---

## v81 - Refinanciamiento

### Epica

Gestion de refinanciamiento de deuda.

### [Backlog] Generacion de plan de refinanciamiento

**Historia de Usuario**

Como analista financiero  
Quiero generar un plan de refinanciamiento  
Para permitir al cliente pagar su deuda en cuotas.

**Criterios de Aceptacion**

- Simular plan de cuotas
- Calcular intereses
- Generar cronograma de pagos
- Registrar acuerdo de refinanciamiento

**Prioridad:** Alta

---

## v82 - Reestructuracion

### Epica

Reestructuracion de obligaciones crediticias.

### [Backlog] Reestructuracion aprobada por comite

**Historia de Usuario**

Como comite de riesgo  
Quiero aprobar reestructuraciones  
Para casos de clientes con dificultades financieras.

**Criterios de Aceptacion**

- Registrar propuesta de reestructuracion
- Registrar aprobacion del comite
- Actualizar condiciones del credito
- Registrar historial del cambio

**Prioridad:** Media

---

## v83 - Convenios de pago

### Epica

Acuerdos especiales de pago.

### [Backlog] Creacion de convenio de pago

**Historia de Usuario**

Como gestor de cobranza  
Quiero generar convenios de pago  
Para formalizar acuerdos con clientes.

**Criterios de Aceptacion**

- Registrar condiciones del convenio
- Generar cronograma de pagos
- Registrar fecha de vigencia
- Permitir seguimiento del cumplimiento

**Prioridad:** Media

---

## v84 - Castigos contables

### Epica

Gestion contable de cartera castigada.

### [Backlog] Ejecucion de castigo contable

**Historia de Usuario**

Como sistema contable  
Quiero marcar una deuda como castigada  
Para reflejar deterioro financiero.

**Criterios de Aceptacion**

- Generar asiento contable
- Cambiar estado de la cuenta
- Registrar motivo de castigo
- Registrar fecha de ejecucion

**Prioridad:** Alta

---

## v85 - Recuperacion post castigo

### Epica

Recuperacion de cartera castigada.

### [Backlog] Registro de pagos de recuperacion

**Historia de Usuario**

Como sistema financiero  
Quiero registrar pagos posteriores al castigo  
Para medir recuperacion de cartera.

**Criterios de Aceptacion**

- Registrar monto recuperado
- Registrar fecha de pago
- Registrar canal de recuperacion
- Actualizar metricas de recuperacion

**Prioridad:** Media

---

## v86 - Evaluacion de aumento de cupo

### Epica

Gestion de incremento de linea de credito.

### [Backlog] Evaluacion automatica de aumento de cupo

**Historia de Usuario**

Como sistema de riesgo  
Quiero evaluar el comportamiento del cliente  
Para ofrecer aumento de cupo.

**Criterios de Aceptacion**

- Analizar historial de pagos
- Analizar uso del credito
- Generar recomendacion de aumento
- Registrar decision

**Prioridad:** Media

---

## v87 - Ofertas preaprobadas

### Epica

Campanas comerciales de tarjetas.

### [Backlog] Generacion de ofertas preaprobadas

**Historia de Usuario**

Como sistema de marketing  
Quiero generar ofertas preaprobadas  
Para incrementar el uso del producto.

**Criterios de Aceptacion**

- Identificar clientes elegibles
- Generar oferta personalizada
- Registrar vigencia
- Permitir aceptacion digital

**Prioridad:** Media

---

## v88 - Onboarding digital

### Epica

Originacion digital de tarjetas.

### [Backlog] Solicitud digital de tarjeta

**Historia de Usuario**

Como cliente  
Quiero solicitar una tarjeta digitalmente  
Para evitar ir a una sucursal.

**Criterios de Aceptacion**

- Formulario digital
- Validacion de identidad
- Registro de solicitud
- Seguimiento de estado

**Prioridad:** Alta

---

## v89 - Evaluacion documental

### Epica

Analisis de solicitudes de tarjeta.

### [Backlog] Evaluacion de documentos

**Historia de Usuario**

Como analista  
Quiero revisar documentacion del cliente  
Para aprobar o rechazar solicitudes.

**Criterios de Aceptacion**

- Validar documentos requeridos
- Registrar observaciones
- Registrar decision final
- Notificar resultado

**Prioridad:** Alta

---

## v90 - Firma digital

### Epica

Formalizacion contractual digital.

### [Backlog] Firma digital de contrato

**Historia de Usuario**

Como cliente  
Quiero firmar digitalmente el contrato  
Para activar mi tarjeta.

**Criterios de Aceptacion**

- Generar contrato digital
- Registrar aceptacion del cliente
- Registrar evidencia de firma
- Activar tarjeta despues de firma

**Prioridad:** Alta

---

## v91 - Multi moneda

### Epica

Soporte de multiples monedas.

### [Backlog] Procesamiento multi moneda

**Historia de Usuario**

Como sistema financiero  
Quiero procesar transacciones en distintas monedas  
Para soportar operaciones internacionales.

**Criterios de Aceptacion**

- Registrar moneda de transaccion
- Calcular conversion
- Registrar tipo de cambio
- Mostrar moneda en estado de cuenta

**Prioridad:** Media

---

## v92 - Multi entidad

### Epica

Arquitectura multi banco.

### [Backlog] Configuracion multi entidad

**Historia de Usuario**

Como plataforma bancaria  
Quiero soportar multiples emisores  
Para operar en distintos paises.

**Criterios de Aceptacion**

- Configurar entidad emisora
- Parametrizar productos por entidad
- Separar datos por entidad

**Prioridad:** Media

---

## v93 - Alta disponibilidad

### Epica

Resiliencia del sistema.

### [Backlog] Failover automatico

**Historia de Usuario**

Como sistema critico  
Quiero mantener disponibilidad  
Para evitar interrupciones del servicio.

**Criterios de Aceptacion**

- Deteccion de fallos
- Cambio automatico de nodo
- Reintentos de procesamiento

**Prioridad:** Alta

---

## v94 - Observabilidad

### Epica

Monitoreo del sistema.

### [Backlog] Trazabilidad transaccional

**Historia de Usuario**

Como equipo de operaciones  
Quiero monitorear transacciones en tiempo real  
Para detectar problemas rapidamente.

**Criterios de Aceptacion**

- Logging estructurado
- Trazas por transaccion
- Metricas operativas
- Dashboards de monitoreo

**Prioridad:** Alta

---

## v95 - Reporting ejecutivo

### Epica

Analitica del negocio de tarjetas.

### [Backlog] Dashboard de negocio

**Historia de Usuario**

Como director del negocio  
Quiero visualizar indicadores del portafolio  
Para tomar decisiones estrategicas.

**Criterios de Aceptacion**

- Metricas de consumo
- Metricas de mora
- Metricas de fraude
- Metricas de recuperacion

**Prioridad:** Media

---

## v96 - Data Lake

### Epica

Plataforma analitica.

### [Backlog] Integracion con Data Lake

**Historia de Usuario**

Como plataforma analitica  
Quiero almacenar datos historicos  
Para analisis avanzado.

**Criterios de Aceptacion**

- Exportacion de eventos
- Almacenamiento historico
- Dataset analitico

**Prioridad:** Media

---

## v97 - Machine Learning antifraude

### Epica

Deteccion avanzada de fraude.

### [Backlog] Modelo predictivo antifraude

**Historia de Usuario**

Como sistema de riesgo  
Quiero detectar fraude mediante modelos ML  
Para mejorar la prevencion.

**Criterios de Aceptacion**

- Score de riesgo
- Integracion con reglas antifraude
- Monitoreo de modelo

**Prioridad:** Media

---

## v98 - Pricing dinamico

### Epica

Gestion avanzada de condiciones comerciales.

### [Backlog] Motor de pricing

**Historia de Usuario**

Como area comercial  
Quiero definir tasas dinamicas  
Para optimizar la rentabilidad del producto.

**Criterios de Aceptacion**

- Configurar tasas
- Configurar promociones
- Vigencia de campanas

**Prioridad:** Media

---

## v99 - Gobierno de datos

### Epica

Gestion de calidad de datos.

### [Backlog] Trazabilidad del dato

**Historia de Usuario**

Como area de gobierno  
Quiero rastrear el origen del dato  
Para cumplir requisitos regulatorios.

**Criterios de Aceptacion**

- Registro de linaje
- Control de calidad
- Auditoria de cambios

**Prioridad:** Media

---

## v100 - Plataforma madura

### Epica

Consolidacion del core bancario.

### [Backlog] Estabilizacion de la plataforma

**Historia de Usuario**

Como arquitectura bancaria  
Quiero consolidar el core  
Para soportar crecimiento futuro.

**Criterios de Aceptacion**

- Revision de arquitectura
- Optimizacion de rendimiento
- Baseline de seguridad
- Documentacion final

**Prioridad:** Alta

## v101 - Open Banking para tarjetas

### Epica

Exposición de APIs para integración con terceros.

### [Backlog] Exposición de APIs de tarjetas

**Historia de Usuario**

Como plataforma bancaria
Quiero exponer APIs de tarjetas
Para permitir integración con fintech.

**Criterios de Aceptacion**

API de consulta de transacciones
API de consulta de límites
API de pagos
Seguridad OAuth2
Control de scopes

**Prioridad:** Alta

## v102 - Tarjetas virtuales dinámicas

### Epica

Seguridad en tarjetas digitales.

### [Backlog] Generación de tarjetas virtuales

**Historia de Usuario**

Como cliente
Quiero generar tarjetas virtuales
Para comprar de forma segura en internet.

**Criterios de Aceptacion**

Generación de PAN virtual
CVV dinámico
Fecha de expiración configurable
Límite por tarjeta virtual
Desactivación automática

**Prioridad:** Alta

## v103 - Tokenización avanzada

### Epica

Protección de datos sensibles.

[Backlog] Tokenización de tarjetas

Historia de Usuario

Como sistema
Quiero tokenizar tarjetas
Para proteger el PAN real.

Criterios de Aceptacion

Generación de token
Asociación token-PAN
Revocación de token
Uso en transacciones
Auditoría de token

Prioridad: Alta

v104 - Integración con wallets
Epica

Ecosistema de pagos digitales.

[Backlog] Integración con billeteras digitales

Historia de Usuario

Como cliente
Quiero usar mi tarjeta en wallets
Para pagar sin tarjeta física.

Criterios de Aceptacion

Integración con Apple Pay
Integración con Google Pay
Activación digital
Tokenización
Validación de dispositivo

Prioridad: Alta

v105 - Motor de loyalty
Epica

Programa de beneficios.

[Backlog] Acumulación de puntos

Historia de Usuario

Como cliente
Quiero acumular puntos por mis compras
Para obtener beneficios.

Criterios de Aceptacion

Generación de puntos por compra
Reglas por MCC
Consulta de puntos
Redención de puntos

Prioridad: Media

v106 - Cashback inteligente
Epica

Beneficios financieros.

[Backlog] Aplicación de cashback

Historia de Usuario

Como cliente
Quiero recibir cashback
Para obtener beneficios económicos.

Criterios de Aceptacion

Cálculo automático
Cashback por categoría
Registro en estado de cuenta
Reglas configurables

Prioridad: Media

v107 - Control de tarjeta por cliente
Epica

Autogestión del cliente.

[Backlog] Control de tarjeta desde app

Historia de Usuario

Como cliente
Quiero controlar mi tarjeta
Para gestionar seguridad y uso.

Criterios de Aceptacion

Bloqueo/desbloqueo
Control por país
Control por comercio
Límites personalizados

Prioridad: Alta

v108 - Autenticación fuerte (3DS)
Epica

Seguridad en ecommerce.

[Backlog] Implementación 3D Secure

Historia de Usuario

Como sistema
Quiero autenticar transacciones online
Para reducir fraude.

Criterios de Aceptacion

3DS 2.x
Autenticación biométrica
Challenge dinámico
Evaluación de riesgo

Prioridad: Alta

v109 - Motor de cuotas
Epica

Financiamiento de compras.

[Backlog] Conversión a cuotas

Historia de Usuario

Como cliente
Quiero diferir mis compras
Para pagarlas en cuotas.

Criterios de Aceptacion

Selección de cuotas
Cálculo de interés
Registro de plan
Visualización en estado de cuenta

Prioridad: Media

v110 - Crédito instantáneo
Epica

Financiamiento inmediato.

[Backlog] Otorgamiento de crédito inmediato

Historia de Usuario

Como cliente
Quiero obtener crédito inmediato
Para financiar mis compras.

Criterios de Aceptacion

Evaluación automática
Aprobación instantánea
Registro del crédito
Desembolso inmediato

Prioridad: Alta

v111 - Marketplace de beneficios
Epica

Ecosistema comercial.

[Backlog] Catálogo de beneficios

Historia de Usuario

Como cliente
Quiero ver beneficios disponibles
Para aprovechar promociones.

Criterios de Aceptacion

Lista de beneficios
Segmentación
Vigencia de promociones
Activación de beneficios

Prioridad: Media

v112 - Personalización con IA
Epica

Experiencia personalizada.

[Backlog] Recomendaciones inteligentes

Historia de Usuario

Como cliente
Quiero recibir recomendaciones
Para mejorar mi experiencia.

Criterios de Aceptacion

Recomendaciones por comportamiento
Ofertas personalizadas
Ajuste dinámico

Prioridad: Media

v113 - Antifraude avanzado
Epica

Detección inteligente de fraude.

[Backlog] Detección de fraude con ML

Historia de Usuario

Como sistema
Quiero detectar fraude
Para reducir pérdidas.

Criterios de Aceptacion

Modelo ML
Score de riesgo
Evaluación en tiempo real
Ajuste de umbrales

Prioridad: Alta

v114 - Gestión avanzada de riesgo
Epica

Control de riesgo crediticio.

[Backlog] Evaluación de riesgo dinámica

Historia de Usuario

Como sistema
Quiero evaluar riesgo
Para controlar exposición crediticia.

Criterios de Aceptacion

Score dinámico
Evaluación por cliente
Evaluación por transacción

Prioridad: Alta

v115 - Optimización de autorizaciones
Epica

Mejora de aprobaciones.

[Backlog] Reducción de rechazos falsos

Historia de Usuario

Como sistema
Quiero optimizar autorizaciones
Para evitar rechazos innecesarios.

Criterios de Aceptacion

Identificación de falsos positivos
Ajuste de reglas
Reintentos inteligentes

Prioridad: Alta

v116 - Simulación de portafolio
Epica

Análisis estratégico.

[Backlog] Simulación financiera

Historia de Usuario

Como negocio
Quiero simular escenarios
Para tomar decisiones.

Criterios de Aceptacion

Simulación de tasas
Simulación de riesgo
Simulación de ingresos

Prioridad: Media

v117 - A/B Testing
Epica

Experimentación.

[Backlog] Pruebas A/B

Historia de Usuario

Como negocio
Quiero probar estrategias
Para mejorar resultados.

Criterios de Aceptacion

Configuración de experimentos
Medición de resultados
Comparación de variantes

Prioridad: Media

v118 - Gestión global del portafolio
Epica

Visión integral del negocio.

[Backlog] Dashboard global

Historia de Usuario

Como directivo
Quiero ver el portafolio completo
Para tomar decisiones estratégicas.

Criterios de Aceptacion

KPIs globales
KPIs por segmento
KPIs por producto

Prioridad: Alta

v119 - Multi región
Epica

Escalabilidad global.

[Backlog] Despliegue multi región

Historia de Usuario

Como plataforma
Quiero operar en múltiples regiones
Para asegurar disponibilidad global.

Criterios de Aceptacion

Replicación geográfica
Balanceo global
Failover regional

Prioridad: Alta

v120 - Plataforma abierta
Epica

Ecosistema bancario abierto.

[Backlog] Plataforma API completa

Historia de Usuario

Como banco
Quiero exponer capacidades
Para integrarme con terceros.

Criterios de Aceptacion

APIs completas
Documentación API
Control de acceso
Monetización de APIs

Prioridad: Alta
