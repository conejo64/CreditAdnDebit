namespace CardVault.Api.Contracts;

public static class EventNames
{
    // Versioned event names (recommended for schema evolution)
    public const string BillingStatementGeneratedV1 = "billing.v1.statement.generated";
    public const string BillingStatementPaymentAppliedV1 = "billing.v1.statement.payment_applied";
    public const string BillingLateFeeAppliedV1 = "billing.v1.statement.late_fee_applied";

    public const string SwitchPurchaseApprovedV1 = "switch.v1.purchase.approved";
    public const string SwitchPurchaseReversedV1 = "switch.v1.purchase.reversed";

    public const string SwitchRefundPostedV1 = "switch.v1.refund.posted";
    public const string SwitchChargebackPostedV1 = "switch.v1.chargeback.posted";

    public const string SwitchAuthApprovedV1 = "switch.v1.auth.approved";
    public const string SwitchAuthReversedV1 = "switch.v1.auth.reversed";
    public const string SwitchClearingPostedV1 = "switch.v1.clearing.posted";

    public const string SettlementBatchCreatedV1 = "settlement.v1.batch.created";
}
