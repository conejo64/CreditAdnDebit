namespace IsoSwitch.Domain;

public static class TransactionTypes
{
    public const string Auth = "AUTH";
    public const string Capture = "CAPTURE";
    public const string ReversalAdvice = "REVERSAL_ADVICE";
}

public static class TransactionStatuses
{
    public const string Pending = "PENDING";
    public const string Confirmed = "CONFIRMED";
    public const string Declined = "DECLINED";
    public const string Captured = "CAPTURED";
    public const string InDoubt = "IN_DOUBT";
}