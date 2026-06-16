namespace CardVault.Domain;

public enum LedgerEntryType
{
    Purchase = 1,
    Payment = 2,
    Fee = 3,
    Interest = 4,
    Adjustment = 5,
    Refund = 6,
    Reversal = 7,
    Chargeback = 8,
    AuthorizationHold = 9,
    Clearing = 10
}
