namespace CardVault.Api.Security;

/// <summary>
/// Binds to the `ConnectionStrings` configuration section purely so
/// <see cref="RequiredConnectionStringsOptionsValidator"/> can fail startup fast
/// when a required connection string is absent (SEC-9). The actual DbContext
/// registrations still read connection strings directly via
/// <c>builder.Configuration.GetConnectionString(...)</c> — this options type
/// exists only to hang <c>ValidateOnStart()</c> off of.
/// </summary>
public sealed class RequiredConnectionStringsOptions
{
    public string? Postgres { get; set; }
    public string? SqlServerIdentity { get; set; }
}
