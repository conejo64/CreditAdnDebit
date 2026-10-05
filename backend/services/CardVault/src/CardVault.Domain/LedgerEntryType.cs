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
    Clearing = 10,

    /// <summary>
    /// Principal of a deferred purchase that has not been billed yet. The original purchase is
    /// reclassified to this type when a plan is created (positive), and each billed installment
    /// posts a negative entry releasing its principal from the bucket. It stays in the account
    /// exposure (available credit) but is excluded from statement balances and interest accrual.
    /// </summary>
    DeferredPrincipal = 11,

    /// <summary>Billed installment principal of a deferred purchase (debit).</summary>
    Installment = 12
}

/// <summary>
/// Sign contract for cardholder ledger entries. The posted balance is the plain sum of
/// <c>Amount</c>, so the sign of every entry must follow its economic direction:
/// <list type="bullet">
///   <item><b>Debit types</b> (<see cref="LedgerEntryType.Purchase"/>, <see cref="LedgerEntryType.Fee"/>,
///   <see cref="LedgerEntryType.Interest"/>, <see cref="LedgerEntryType.Clearing"/>,
///   <see cref="LedgerEntryType.Installment"/>) increase the cardholder's debt and are stored
///   <b>positive</b>.</item>
///   <item><b>Credit types</b> (<see cref="LedgerEntryType.Payment"/>, <see cref="LedgerEntryType.Refund"/>,
///   <see cref="LedgerEntryType.Reversal"/>, <see cref="LedgerEntryType.Chargeback"/>) reduce the
///   cardholder's debt and are stored <b>negative</b>.</item>
///   <item><b>Signed types</b> (<see cref="LedgerEntryType.Adjustment"/>, <see cref="LedgerEntryType.AuthorizationHold"/>,
///   <see cref="LedgerEntryType.DeferredPrincipal"/>) may legitimately go either way (a hold is placed
///   positive and released with a negative shadow entry; deferred principal is parked positive and
///   released negative as installments are billed) and keep the sign supplied by the caller.</item>
/// </list>
/// <see cref="DeferredPrincipal"/> is the one type that belongs to the account exposure but not to the
/// billable balance: statement balances and interest accrual exclude it (see <see cref="IsBillable"/>),
/// while available credit keeps counting it.
/// <see cref="NormalizeAmount"/> applies the contract to an incoming amount so callers may pass
/// either sign and still get a correct ledger.
/// </summary>
public static class LedgerEntryTypeExtensions
{
    public static bool IsDebit(this LedgerEntryType type) => type is
        LedgerEntryType.Purchase or
        LedgerEntryType.Fee or
        LedgerEntryType.Interest or
        LedgerEntryType.Clearing or
        LedgerEntryType.Installment;

    /// <summary>
    /// True for every entry that is part of the balance the cardholder is billed for and pays
    /// interest on. Only <see cref="LedgerEntryType.DeferredPrincipal"/> is excluded: it is owed,
    /// and consumes credit line, but is billed later through <see cref="LedgerEntryType.Installment"/>.
    /// </summary>
    public static bool IsBillable(this LedgerEntryType type) => type != LedgerEntryType.DeferredPrincipal;

    public static bool IsCredit(this LedgerEntryType type) => type is
        LedgerEntryType.Payment or
        LedgerEntryType.Refund or
        LedgerEntryType.Reversal or
        LedgerEntryType.Chargeback;

    /// <summary>
    /// Returns <paramref name="amount"/> with the sign mandated by the contract for
    /// <paramref name="type"/>: positive for debit types, negative for credit types, unchanged
    /// for signed types.
    /// </summary>
    public static decimal NormalizeAmount(this LedgerEntryType type, decimal amount)
    {
        if (type.IsDebit()) return Math.Abs(amount);
        if (type.IsCredit()) return -Math.Abs(amount);
        return amount;
    }
}
