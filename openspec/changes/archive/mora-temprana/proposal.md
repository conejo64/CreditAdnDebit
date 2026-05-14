# Propuesta de Cambio: Mora Temprana (v76)

## 1. Intención
Implementar la primera etapa de la gestión de cartera vencida (Mora Temprana - v76). El objetivo es que el sistema identifique automáticamente las cuentas que no han cubierto su pago mínimo en la fecha de vencimiento, marque la cuenta como morosa (`DELINQUENT`), clasifique los días de atraso en rangos (buckets) y mantenga un historial para futuras acciones de cobranza.

## 2. Alcance
- **Dominio:** Creación de la entidad `DelinquencyRecord` para historizar la mora.
- **Logica de Negocio:** Implementación de `EvaluateDelinquencyCommand` para procesar cuentas vencidas.
- **Worker:** Creación de un `BackgroundService` diario (`DelinquencyEvaluationWorker`) que ejecute la evaluación.
- **Frontend (Opcional por ahora):** Aunque el foco es backend, el API debe exponer endpoints para consultar cuentas en mora.

## 3. Diseño Arquitectónico

### Nuevas Entidades
*   `DelinquencyRecordEntity`:
    *   `Id` (Guid)
    *   `AccountId` (Guid)
    *   `StatementId` (Guid) - El estado de cuenta que originó la mora.
    *   `OverdueAmount` (Decimal) - El monto no pagado (MinimumPayment - Payments).
    *   `DaysInArrears` (Int) - Días de mora.
    *   `Bucket` (String) - `1_TO_30`, `31_TO_60`, `61_TO_90`, `OVER_90`.
    *   `Status` (String) - `ACTIVE` (aún en mora), `RESOLVED` (pagado).

### Lógica de Evaluación (Reglas)
*   Se evalúan cuentas donde `DueDate` < `Today`.
*   Si no se ha cubierto el `MinimumPayment`, la cuenta pasa a `Status = DELINQUENT`.
*   Se calcula `DaysInArrears = Today - DueDate`.
*   El `Bucket` se asigna según los días.
*   Si la cuenta ya tenía un registro `ACTIVE`, se actualiza (días y bucket). Si paga, se marca `RESOLVED` y la cuenta vuelve a `ACTIVE`.

### Integración
*   Se usará un `IHostedService` (`BackgroundService`) dentro de `CardVault.Api/Background/` que se ejecute una vez al día (usando un temporizador) para despachar el `EvaluateDelinquencyCommand`.

## 4. Riesgos y Rollback
- **Riesgo:** Un error en la evaluación diaria podría marcar cuentas al día como morosas, afectando la disponibilidad de la tarjeta para el cliente.
- **Mitigación:** TDD estricto para los casos límite (pago exacto, pago parcial, feriados). 
- **Rollback:** Las actualizaciones de estado y creación de registros de mora se harán de forma idempotente. En caso de falla, se puede hacer un revert del commit y correr un script de compensación sobre la tabla `DelinquencyRecord`.
