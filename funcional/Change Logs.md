?# Backlog Funcional
## Core Bancario de Tarjetas de Crédito y Débito
### Versiones v56 – v65

---

# v56 – Motor de Autorización Transaccional

## Épica
Motor de autorización del switch transaccional.

---

### **[Backlog] Autorización de compras nacionales**

**Historia de Usuario**

Como sistema de autorización del switch  
Quiero validar las transacciones de compra realizadas con tarjetas  
Para determinar si la transacción debe ser aprobada o rechazada.

**Criterios de Aceptación**

- Validar estado de tarjeta
- Validar fecha de vencimiento
- Validar cupo disponible
- Validar límites por transacción
- Registrar respuesta de autorización

**Prioridad:** Alta

---

### **[Backlog] Validación de estado de tarjeta**

**Historia de Usuario**

Como motor transaccional  
Quiero validar el estado de la tarjeta  
Para evitar autorizaciones con tarjetas bloqueadas o vencidas.

**Criterios de Aceptación**

- Tarjeta activa → permitir validación
- Tarjeta bloqueada → rechazar
- Tarjeta vencida → rechazar
- Tarjeta cancelada → rechazar

**Prioridad:** Alta

---

### **[Backlog] Control de límites transaccionales**

**Historia de Usuario**

Como sistema de autorización  
Quiero validar límites de transacción  
Para evitar consumos superiores a los permitidos.

**Criterios de Aceptación**

- Validar monto máximo por transacción
- Validar número de transacciones diarias
- Validar monto acumulado diario
- Rechazar si excede límite

**Prioridad:** Alta

---

### **[Backlog] Registro de trazabilidad de autorizaciones**

**Historia de Usuario**

Como sistema de auditoría  
Quiero registrar todas las autorizaciones  
Para garantizar trazabilidad operativa.

**Criterios de Aceptación**

- Registrar request del switch
- Registrar respuesta generada
- Guardar fecha y hora
- Guardar canal y comercio

**Prioridad:** Alta

---

# v57 – Reversos y anulaciones

## Épica
Gestión de reversos y control de duplicidad transaccional.

---

### **[Backlog] Procesamiento de reversos**

**Historia de Usuario**

Como sistema transaccional  
Quiero procesar reversos enviados por el comercio o switch  
Para revertir una autorización previamente aprobada.

**Criterios de Aceptación**

- Identificar transacción original
- Validar ventana de reverso
- Liberar cupo correspondiente
- Registrar reverso

**Prioridad:** Alta

---

### **[Backlog] Control de idempotencia**

**Historia de Usuario**

Como motor transaccional  
Quiero evitar el procesamiento duplicado de transacciones  
Para prevenir inconsistencias contables.

**Criterios de Aceptación**

- Identificar transacciones repetidas
- Evitar duplicación
- Retornar respuesta previa

**Prioridad:** Alta

---

### **[Backlog] Manejo de timeouts**

**Historia de Usuario**

Como switch transaccional  
Quiero controlar respuestas tardías  
Para evitar duplicación de transacciones.

**Criterios de Aceptación**

- Controlar tiempo máximo de respuesta
- Registrar timeout
- Permitir reintento seguro

**Prioridad:** Media

---

# v58 – Retiros ATM y avances de efectivo [Completado]

## Épica
Autorización de transacciones ATM.

---

### **[Backlog] Autorización de retiros ATM**

**Historia de Usuario**

Como cliente del banco  
Quiero retirar dinero desde un cajero automático  
Para acceder a mis fondos disponibles.

**Criterios de Aceptación**

- Validar PIN
- Validar saldo disponible
- Validar límites ATM
- Registrar transacción

**Prioridad:** Alta

---

### **[Backlog] Comisión por avance de efectivo**

**Historia de Usuario**

Como sistema de tarjetas  
Quiero calcular comisión por avances de efectivo  
Para aplicar cargos correspondientes.

**Criterios de Aceptación**

- Calcular comisión según producto
- Registrar cargo en cuenta
- Mostrar detalle en estado de cuenta

**Prioridad:** Media

---

### **[Backlog] Bloqueo por intentos fallidos de PIN**

**Historia de Usuario**

Como sistema de seguridad  
Quiero bloquear tarjetas tras múltiples PIN incorrectos  
Para proteger la seguridad del cliente.

**Criterios de Aceptación**

- Configurar número máximo de intentos
- Bloquear tarjeta automáticamente
- Registrar evento de seguridad

**Prioridad:** Alta

---

# v59 – Compensación y liquidación [Completado]

## Épica
Procesos de conciliación transaccional.

---

### **[Backlog] Recepción de archivos de compensación**

**Historia de Usuario**

Como sistema de tarjetas  
Quiero recibir archivos de compensación  
Para procesar liquidaciones de transacciones.

**Criterios de Aceptación**

- Importar archivos de compensación
- Validar estructura
- Registrar transacciones

**Prioridad:** Alta

---

### **[Backlog] Conciliación de transacciones**

**Historia de Usuario**

Como sistema contable  
Quiero conciliar transacciones autorizadas con compensadas  
Para detectar inconsistencias.

**Criterios de Aceptación**

- Identificar transacciones faltantes
- Identificar duplicados
- Generar reporte de conciliación

**Prioridad:** Alta

---

# v60 – Motor antifraude

## Épica
Prevención de fraude en tarjetas.

---

### **[Backlog] Reglas antifraude**

**Historia de Usuario**

Como sistema antifraude  
Quiero analizar patrones de transacciones  
Para detectar actividades sospechosas.

**Criterios de Aceptación**

- Analizar ubicación geográfica
- Analizar frecuencia de transacciones
- Generar alerta de riesgo

**Prioridad:** Alta

---

### **[Backlog] Bloqueo preventivo de tarjeta**

**Historia de Usuario**

Como sistema antifraude  
Quiero bloquear tarjetas sospechosas  
Para prevenir fraude.

**Criterios de Aceptación**

- Activar bloqueo automático
- Notificar al sistema de monitoreo
- Registrar evento

**Prioridad:** Alta

---

# v61 – Ciclo de vida de tarjeta [Completado]

## Épica
Administración de tarjetas.

---

### **[Backlog] Emisión de tarjeta**

**Historia de Usuario**

Como banco  
Quiero emitir tarjetas de crédito y débito  
Para habilitar a los clientes a realizar transacciones.

**Criterios de Aceptación**

- Registrar tarjeta
- Asociar cliente
- Asociar cuenta
- Generar número de tarjeta

**Prioridad:** Alta

---

### **[Backlog] Bloqueo y desbloqueo de tarjeta**

**Historia de Usuario**

Como operador del banco  
Quiero bloquear o desbloquear tarjetas  
Para gestionar incidentes o solicitudes de clientes.

**Criterios de Aceptación**

- Registrar motivo
- Cambiar estado de tarjeta
- Guardar historial

**Prioridad:** Alta

---

# v62 – Facturación de tarjetas de crédito [Completado]

## Épica
Gestión financiera de tarjetas de crédito.

---

### **[Backlog] Generación de estado de cuenta**

**Historia de Usuario**

Como cliente  
Quiero recibir mi estado de cuenta mensual  
Para conocer mis consumos y saldo pendiente.

**Criterios de Aceptación**

- Listar transacciones
- Calcular saldo
- Calcular pago mínimo
- Calcular pago total

**Prioridad:** Alta

---

### **[Backlog] Cálculo de intereses rotativos**

**Historia de Usuario**

Como sistema financiero  
Quiero calcular intereses sobre saldo financiado  
Para generar cargos correspondientes.

**Criterios de Aceptación**

- Aplicar tasa de interés
- Calcular días financiados
- Registrar interés generado

**Prioridad:** Alta

---

# v63 – Pagos de tarjeta [Completado]

## Épica
Procesamiento de pagos de tarjetas.

---

### **[Backlog] Registro de pagos**

**Historia de Usuario**

Como cliente  
Quiero pagar mi tarjeta de crédito  
Para reducir mi saldo pendiente.

**Criterios de Aceptación**

- Registrar pago
- Actualizar saldo
- Liberar cupo disponible

**Prioridad:** Alta

---

# v64 – Reclamos y contracargos [Completado]

## Épica
Gestión de disputas.

---

### **[Backlog] Registro de reclamos**

**Historia de Usuario**

Como cliente  
Quiero reportar transacciones desconocidas  
Para iniciar investigación.

**Criterios de Aceptación**

- Registrar reclamo
- Asociar transacción
- Generar caso de investigación

**Prioridad:** Alta

---

# v65 – Integración contable

## Épica
Integración con contabilidad bancaria.

---

### **[Backlog] Generación de asientos contables**

**Historia de Usuario**

Como sistema contable  
Quiero registrar movimientos contables  
Para reflejar operaciones de tarjetas.

**Criterios de Aceptación**

- Generar asiento contable
- Asociar transacción
- Registrar fecha contable

**Prioridad:** Alta

Core Bancario de Tarjetas – Backlog Funcional
Versiones v66 – v75
# v66 – Compras en cuotas y diferidos [Completado]
## Épica

Gestión de compras diferidas y financiamiento en cuotas.

### **[Backlog] Compra en cuotas en punto de venta**

**Historia de Usuario**

Como cliente del banco
Quiero poder diferir una compra en cuotas
Para pagar el consumo en varios meses.

**Criterios de Aceptación**

- Permitir seleccionar número de cuotas
- Validar cupo disponible
- Calcular monto por cuota
- Registrar plan de cuotas
- Reflejar cuotas en estado de cuenta

**Prioridad:** Alta

### **[Backlog] Diferido posterior a la compra**

**Historia de Usuario**

Como cliente
Quiero diferir una compra ya realizada
Para convertirla en cuotas.

**Criterios de Aceptación**

- Seleccionar transacción elegible
- Definir número de cuotas
- Calcular intereses
- Registrar plan de financiamiento

**Prioridad:** Alta

### **[Backlog] Parametrización de tasas de cuotas**

**Historia de Usuario**

Como administrador del banco
Quiero definir tasas de financiamiento por cuotas
Para controlar las condiciones financieras del producto.

**Criterios de Aceptación**

- Configurar tasa por producto
- Configurar tasa por plazo
- Permitir promociones

**Prioridad:** Media

### **v67 – Cashback y programas de beneficios**
Épica

Gestión de programas de recompensas.

### **[Backlog] Programa de cashback**

**Historia de Usuario**

Como cliente
Quiero recibir cashback por mis compras
Para obtener beneficios por el uso de mi tarjeta.

**Criterios de Aceptación**

- Calcular porcentaje de cashback
- Registrar acumulación
- Permitir redención

**Prioridad:** Alta

### **[Backlog] Acumulación de puntos**

**Historia de Usuario**

Como cliente
Quiero acumular puntos por cada compra
Para canjearlos por beneficios.

**Criterios de Aceptación**

- Calcular puntos por transacción
- Registrar saldo de puntos
- Permitir consulta

**Prioridad:** Media

### **[Backlog] Catálogo de recompensas**

**Historia de Usuario**

Como cliente
Quiero ver los beneficios disponibles
Para canjear mis puntos o cashback.

**Criterios de Aceptación**

- Listar recompensas
- Mostrar costo en puntos
- Permitir redención

**Prioridad:** Media

### **v68 – Tokenización de tarjetas**
Épica

Seguridad avanzada en pagos digitales.

### **[Backlog] Generación de token de tarjeta**

**Historia de Usuario**

Como sistema de pagos
Quiero generar un token seguro de la tarjeta
Para evitar exponer el número real de la tarjeta.

**Criterios de Aceptación**

- Generar token único
- Asociar token con tarjeta
- Permitir uso en pagos digitales

**Prioridad:** Alta

### **[Backlog] Validación de token**

**Historia de Usuario**

Como sistema transaccional
Quiero validar tokens en lugar del número de tarjeta
Para aumentar la seguridad.

**Criterios de Aceptación**

- Validar token activo
- Obtener tarjeta asociada
- Procesar transacción

**Prioridad:** Alta

### **v69 – Integración con billeteras digitales**
Épica

Pagos con wallets digitales.

### **[Backlog] Registro de tarjeta en wallet**

**Historia de Usuario**

Como cliente
Quiero registrar mi tarjeta en una billetera digital
Para pagar con mi teléfono.

**Criterios de Aceptación**

- Registrar token de tarjeta
- Validar autenticación
- Activar tarjeta en wallet

**Prioridad:** Alta

### **[Backlog] Autorización de pagos wallet**

**Historia de Usuario**

Como sistema transaccional
Quiero autorizar pagos realizados desde wallets
Para permitir pagos móviles.

**Criterios de Aceptación**

- Validar token
- Validar autenticación
- Procesar autorización

**Prioridad:** Alta

### **v70 – Pagos e-commerce seguros**
Épica

Seguridad para pagos en línea.

### **[Backlog] Autenticación 3D Secure**

**Historia de Usuario**

Como cliente
Quiero validar mi identidad en compras online
Para evitar fraude.

**Criterios de Aceptación**

- Enviar desafío de autenticación
- Validar OTP

**Autorizar o rechazar transacción**

**Prioridad:** Alta

### **[Backlog] Evaluación de riesgo e-commerce**

**Historia de Usuario**

Como sistema antifraude
Quiero analizar transacciones online
Para detectar fraude potencial.

**Criterios de Aceptación**

- Analizar ubicación
- Analizar historial
- Generar score de riesgo

**Prioridad:** Alta

### **v71 – Sobregiros y ampliación de cupo**
Épica

Gestión dinámica de cupos.

### **[Backlog] Sobregiro autorizado**

**Historia de Usuario**

Como cliente
Quiero poder exceder mi cupo bajo ciertas condiciones
Para completar una compra importante.

**Criterios de Aceptación**

- Validar límite de sobregiro
- Registrar exceso de cupo
- Aplicar comisión

**Prioridad:** Media

### **[Backlog] Incremento automático de cupo**

**Historia de Usuario**

Como sistema de tarjetas
Quiero evaluar comportamiento del cliente
Para ofrecer aumentos de cupo.

**Criterios de Aceptación**

- Evaluar historial de pagos
- Evaluar uso del crédito
- Proponer aumento

**Prioridad:** Media

### **v72 – Motor de campañas comerciales**
Épica

Marketing y fidelización.

### **[Backlog] Campañas de promociones**

**Historia de Usuario**

Como área comercial
Quiero lanzar promociones en comercios específicos
Para incentivar el uso de la tarjeta.

Criterios de Aceptación

Configurar comercios

Configurar descuento

Registrar campaña

**Prioridad:** Media

### **[Backlog] Ofertas personalizadas**

**Historia de Usuario**

Como sistema de marketing
Quiero generar ofertas según comportamiento del cliente
Para mejorar la conversión.

Criterios de Aceptación

Analizar historial de compras

Generar oferta personalizada

Prioridad: Media

### **v73 – Open Banking**
Épica

Exposición de APIs bancarias.

### **[Backlog] API de consulta de transacciones**

Historia de Usuario

Como aplicación externa autorizada
Quiero consultar transacciones de tarjeta
Para mostrar información al cliente.

Criterios de Aceptación

Autenticación OAuth

Retornar movimientos

Registrar auditoría

Prioridad: Alta

### **[Backlog] API de consulta de saldo**

**Historia de Usuario**

Como aplicación financiera
Quiero consultar el saldo de la tarjeta
Para mostrar información actualizada.

**Criterios de Aceptación**

- Retornar saldo actual
- Validar autorización

**Prioridad:** Alta

### **v74 – Notificaciones al cliente**
Épica

Comunicación con clientes.

### **[Backlog] Notificación de transacciones**

**Historia de Usuario**

Como cliente
Quiero recibir notificaciones por cada transacción
Para monitorear el uso de mi tarjeta.

**Criterios de Aceptación**

- Enviar notificación
- Incluir monto
- Incluir comercio

**Prioridad:** Alta

### **[Backlog] Alertas de seguridad**

**Historia de Usuario**

Como cliente
Quiero recibir alertas cuando exista actividad sospechosa
Para reaccionar rápidamente.

**Criterios de Aceptación**

- Detectar actividad sospechosa
- Enviar alerta

**Prioridad:** Alta

### **v75 – Analytics del negocio de tarjetas**
Épica

Inteligencia de negocio.

### **[Backlog] Dashboard de consumo**

**Historia de Usuario**

Como área de negocio
Quiero visualizar consumo por categoría
Para analizar comportamiento de clientes.

Criterios de Aceptación

Agrupar transacciones

Mostrar gráficos

Prioridad: Media

### **[Backlog] Análisis de fraude

**Historia de Usuario**

Como área de riesgo
Quiero analizar patrones de fraude
Para mejorar reglas antifraude.

Criterios de Aceptación

Generar reportes

Analizar tendencias

Prioridad: Media
### v76 � Optimizaci�n y Administraci�n Global [Completado]
Epica

Mejora de la experiencia de usuario y capacidades administrativas globales.

### **[Backlog] Administraci�n Maestra de Cuentas y Tarjetas**

**Historia de Usuario**

Como administrador del sistema  
Quiero disponer de una vista global de todas las cuentas y tarjetas  
Para realizar operaciones administrativas sin necesidad de navegar al detalle del cliente.

**Criterios de Aceptaci�n**

- Bot�n de apertura de cuenta global en el listado maestro.
- Botones de acci�n directa (Bloqueo, Activaci�n, Cambio de PIN) en tablas de listado.
- Selecci�n din�mica de clientes y productos desde el modal global.

**Prioridad:** Alta

---

### **[Backlog] Optimizaci�n de Performance Frontend/Backend**

**Historia de Usuario**

Como usuario del portal  
Quiero que las pantallas carguen r�pidamente y las b�squedas sean fluidas  
Para mejorar la eficiencia operativa.

**Criterios de Aceptaci�n**

- Implementar debouncing (400ms) en todos los inputs de b�squeda global.
- Eliminar loops anidados O(N x M) en frontend para asociaci�n de nombres.
- Optimizar queries de backend con AsNoTracking() y proyecciones directas.
- Reducir la dependencia de forkJoin bloqueante en la carga de componentes.

**Prioridad:** Alta

