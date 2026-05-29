namespace CardVault.Api.Security;

/// <summary>
/// Catálogo centralizado de todos los permisos granulares del sistema.
/// Cada constante corresponde al valor del claim "perm" que otorga acceso
/// equivalente a la política de autorización nombrada.
/// </summary>
public static class PermissionCatalog
{
    public const string IssuerOperate        = "issuer:operate";
    public const string RoutingManage        = "routing:manage";
    public const string CardsManage          = "cards:manage";
    public const string CreditPoliciesManage = "credit-policies:manage";
    public const string LedgerView           = "ledger:view";
    public const string LedgerOperate        = "ledger:operate";
    public const string BillingView          = "billing:view";
    public const string BillingOperate       = "billing:operate";
    public const string BillingPoliciesManage = "billing-policies:manage";
    public const string RiskManage           = "risk:manage";
    public const string DisputesView         = "disputes:view";
    public const string DisputesManage       = "disputes:manage";
    public const string SettlementView       = "settlement:view";
    public const string SettlementRun        = "settlement:run";
    public const string SwitchOperate        = "switch:operate";
    public const string SwitchMonitor        = "switch:monitor";
    public const string AuditView            = "audit:view";
    public const string VaultRotateKeys      = "vault:rotate-keys";
    public const string UsersManage          = "users:manage";
    public const string VaultDetokenize      = "vault:detokenize";
    public const string AnalyticsView        = "analytics:view";
    public const string LoyaltyView          = "loyalty:view";
    public const string LoyaltyManage        = "loyalty:manage";
    public const string WalletsManage        = "wallets:manage";
    public const string WalletsPay           = "wallets:pay";
    public const string CreditLimitsView     = "credit-limits:view";
    public const string CreditLimitsManage   = "credit-limits:manage";
    public const string AccountingView       = "accounting:view";
    public const string AccountingManage     = "accounting:manage";
    public const string CollectionsView      = "collections:view";
    public const string CollectionsManage    = "collections:manage";

    public static readonly IReadOnlyList<string> All =
    [
        IssuerOperate,
        RoutingManage,
        CardsManage,
        CreditPoliciesManage,
        LedgerView,
        LedgerOperate,
        BillingView,
        BillingOperate,
        BillingPoliciesManage,
        RiskManage,
        DisputesView,
        DisputesManage,
        SettlementView,
        SettlementRun,
        SwitchOperate,
        SwitchMonitor,
        AuditView,
        VaultRotateKeys,
        UsersManage,
        VaultDetokenize,
        AnalyticsView,
        LoyaltyView,
        LoyaltyManage,
        WalletsManage,
        WalletsPay,
        CreditLimitsView,
        CreditLimitsManage,
        AccountingView,
        AccountingManage,
        CollectionsView,
        CollectionsManage
    ];

    public static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [IssuerOperate]         = "Operar emisor: activar/desactivar tarjetas, cuentas, PINs",
            [RoutingManage]         = "Gestionar rutas de switch y reglas de enrutamiento",
            [CardsManage]           = "Emitir y gestionar tarjetas físicas y virtuales",
            [CreditPoliciesManage]  = "Configurar políticas de crédito y parámetros de producto",
            [LedgerView]            = "Ver movimientos del ledger contable",
            [LedgerOperate]         = "Registrar asientos y operaciones en el ledger",
            [BillingView]           = "Ver estados de cuenta, ciclos y cargos",
            [BillingOperate]        = "Ejecutar facturación y aplicar pagos",
            [BillingPoliciesManage] = "Configurar políticas de facturación y mínimos",
            [RiskManage]            = "Gestionar reglas de riesgo, velocidad y MCC",
            [DisputesView]          = "Consultar disputas y chargebacks",
            [DisputesManage]        = "Gestionar el ciclo de vida de disputas",
            [SettlementView]        = "Consultar batches de liquidación y compensación",
            [SettlementRun]         = "Ejecutar y cerrar procesos de liquidación",
            [SwitchOperate]         = "Ejecutar autorizaciones, capturas, reversos y simulaciones controladas del switch ISO 8583",
            [SwitchMonitor]         = "Ver monitor de transacciones del switch ISO 8583",
            [AuditView]             = "Acceder al log de auditoría PCI-safe",
            [VaultRotateKeys]       = "Rotar claves de cifrado del vault de tokenización",
            [UsersManage]           = "Crear, editar y gestionar usuarios y roles",
            [VaultDetokenize]       = "Destokenizar PANs (acceso sensible a datos de tarjeta)",
            [AnalyticsView]         = "Ver reportes de analítica y dashboard de negocio",
            [LoyaltyView]           = "Consultar programas de fidelización y saldos",
            [LoyaltyManage]         = "Gestionar programas de recompensas y catálogo",
            [WalletsManage]         = "Administrar tokens de wallet y enrollments",
            [WalletsPay]            = "Procesar pagos con wallet (Apple Pay, Google Pay, etc.)",
            [CreditLimitsView]      = "Consultar cupos de crédito y propuestas",
            [CreditLimitsManage]    = "Modificar cupos y aprobar propuestas de incremento",
            [AccountingView]        = "Consultar journals contables y mapeos",
            [AccountingManage]      = "Configurar mapeos contables y revisar journals",
            [CollectionsView]       = "Consultar cuentas en mora temprana y sus buckets de antigüedad",
        [CollectionsManage]     = "Registrar intentos de contacto y notas internas en cuentas en mora",
        };

}
