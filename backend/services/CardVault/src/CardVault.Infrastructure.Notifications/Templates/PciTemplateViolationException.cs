namespace CardVault.Infrastructure.Notifications.Templates;

/// <summary>
/// Thrown by <see cref="PciTemplateGuard.Validate"/> when a <see cref="TemplateModel"/>
/// contains sensitive data that must not reach the template renderer.
/// <para>
/// SECURITY: The exception message intentionally does NOT echo back the offending value
/// to prevent sensitive data from appearing in logs or error responses.
/// </para>
/// </summary>
public sealed class PciTemplateViolationException : Exception
{
    /// <summary>Name of the model field that triggered the guard.</summary>
    public string FieldName { get; }

    /// <summary>The guard rule that was violated.</summary>
    public string Rule { get; }

    /// <summary>
    /// Creates a new <see cref="PciTemplateViolationException"/>.
    /// The offending value is deliberately NOT included in the message.
    /// </summary>
    public PciTemplateViolationException(string fieldName, string rule)
        : base($"PCI template guard violation on field '{fieldName}': {rule}. " +
               "The offending value is not echoed to prevent sensitive data leakage.")
    {
        FieldName = fieldName;
        Rule = rule;
    }
}
