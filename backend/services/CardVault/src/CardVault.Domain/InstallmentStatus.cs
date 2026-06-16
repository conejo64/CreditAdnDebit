namespace CardVault.Domain;

public enum InstallmentStatus
{
    Pending = 1,
    Invoiced = 2, // Puesto en el estado de cuenta
    Paid = 3,
    Skipped = 4
}
