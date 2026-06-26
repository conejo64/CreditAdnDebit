namespace IsoSwitch.Domain;

public static class TransactionStateMachine
{
    // Simple, explicit transitions per TxType.
    public static bool CanTransition(string txType, string from, string to)
    {
        txType = txType?.Trim().ToUpperInvariant() ?? "";
        from = from?.Trim().ToUpperInvariant() ?? "";
        to = to?.Trim().ToUpperInvariant() ?? "";

        return txType switch
        {
            TransactionTypes.Auth => from switch
            {
                TransactionStatuses.Pending => to is TransactionStatuses.Confirmed or TransactionStatuses.Declined,
                _ => false
            },
            TransactionTypes.Capture => from switch
            {
                TransactionStatuses.Pending => to is TransactionStatuses.Captured or TransactionStatuses.Declined,
                _ => false
            },
            TransactionTypes.ReversalAdvice => from switch
            {
                TransactionStatuses.Pending => to is TransactionStatuses.Confirmed or TransactionStatuses.Declined,
                _ => false
            },
            _ => false
        };
    }

    public static void EnsureTransition(string txType, string from, string to)
    {
        if (!CanTransition(txType, from, to))
            throw new InvalidOperationException($"Invalid transition for {txType}: {from} -> {to}");
    }
}