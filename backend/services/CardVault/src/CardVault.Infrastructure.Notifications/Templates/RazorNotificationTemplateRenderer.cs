using RazorLight;

namespace CardVault.Infrastructure.Notifications.Templates;

/// <summary>
/// <see cref="INotificationTemplateRenderer"/> implementation using RazorLight.
/// Templates are plain files on disk, located at
/// <c>Templates/{TemplateType}.{locale}.cshtml</c>
/// relative to the application base directory.
/// <para>
/// PCI contract: <see cref="PciTemplateGuard"/> runs BEFORE render; rendered body is NEVER logged.
/// </para>
/// </summary>
public sealed class RazorNotificationTemplateRenderer : INotificationTemplateRenderer
{
    private const string FallbackLocale = "es-EC";
    private static readonly HashSet<string> SupportedLocales =
        new(StringComparer.OrdinalIgnoreCase) { "es-EC", "en-US" };

    private readonly IRazorLightEngine _engine;
    private readonly PciTemplateGuard _guard;
    private readonly string _templatesPath;

    /// <summary>
    /// Constructs the renderer with an injected engine and PCI guard (for testability).
    /// </summary>
    public RazorNotificationTemplateRenderer(IRazorLightEngine engine, PciTemplateGuard guard, string templatesPath)
    {
        _engine = engine;
        _guard = guard;
        _templatesPath = templatesPath;
    }

    /// <summary>
    /// Creates a production-configured renderer using <see cref="AppContext.BaseDirectory"/>
    /// as the templates root. Pass <paramref name="templatesPath"/> to override (e.g., in tests).
    /// </summary>
    public static RazorNotificationTemplateRenderer Create(string? templatesPath = null)
    {
        var path = templatesPath
            ?? Path.Combine(AppContext.BaseDirectory, "Templates");

        var engine = new RazorLightEngineBuilder()
            .UseFileSystemProject(path)
            .UseMemoryCachingProvider()
            .Build();

        return new RazorNotificationTemplateRenderer(engine, new PciTemplateGuard(), path);
    }

    /// <inheritdoc />
    public async Task<RenderedTemplate> RenderAsync(
        TemplateModel model,
        CancellationToken ct = default)
    {
        // PCI guard MUST run before we ever touch the Razor engine
        _guard.Validate(model);

        var locale = NormalizeLocale(model.Locale);

        // Template key for FileSystemProject: filename without extension
        // File: {_templatesPath}/{TemplateType}.{locale_underscore}.cshtml
        // Key:  {TemplateType}.{locale_underscore}
        var templateKey = BuildTemplateKey(model.TemplateType, locale);

        // Fall back to es-EC if the requested locale template does not exist on disk
        if (!TemplateExists(model.TemplateType, locale) && locale != FallbackLocale)
        {
            locale = FallbackLocale;
            templateKey = BuildTemplateKey(model.TemplateType, locale);
        }

        var resolvedModel = model with { Locale = locale };

        // RazorLight caches compiled templates; rendered body is NEVER logged (PCI)
        var body = await _engine
            .CompileRenderAsync(templateKey, resolvedModel)
            .ConfigureAwait(false);

        var subject = BuildSubject(model.TemplateType, locale, resolvedModel);

        return new RenderedTemplate(subject, body);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
            return FallbackLocale;
        return SupportedLocales.Contains(locale) ? locale : FallbackLocale;
    }

    private static string BuildTemplateKey(string templateType, string locale)
    {
        // FileSystemProject looks for {key}.cshtml in the configured root directory.
        // We replace hyphens with underscores to match the filename convention (es_EC, en_US).
        var localeKey = locale.Replace("-", "_");
        return $"{templateType}.{localeKey}";
    }

    private bool TemplateExists(string templateType, string locale)
    {
        var localeKey = locale.Replace("-", "_");
        var fileName = $"{templateType}.{localeKey}.cshtml";
        return File.Exists(Path.Combine(_templatesPath, fileName));
    }

    private static string BuildSubject(string templateType, string locale, TemplateModel model)
    {
        return (templateType, locale) switch
        {
            ("Otp", "en-US") => "Your verification code",
            ("Otp", _)       => "Tu código de verificación",

            ("TransactionNotification", "en-US") => BuildTransactionSubjectEnUs(model),
            ("TransactionNotification", _)       => BuildTransactionSubjectEsEc(model),

            ("SecurityAlert", "en-US") => "Security alert on your account",
            ("SecurityAlert", _)       => "Alerta de seguridad en tu cuenta",

            ("StatementAvailable", "en-US") => "Your statement is available",
            ("StatementAvailable", _)       => "Tu estado de cuenta está disponible",

            ("PaymentReceived", "en-US") => "Payment received",
            ("PaymentReceived", _)       => "Pago recibido",

            _ => templateType
        };
    }

    private static string BuildTransactionSubjectEsEc(TemplateModel model)
    {
        if (model.Amount.HasValue && !string.IsNullOrEmpty(model.CurrencyCode))
            return $"Transacción de {model.Amount:F2} {model.CurrencyCode}";
        return "Notificación de transacción";
    }

    private static string BuildTransactionSubjectEnUs(TemplateModel model)
    {
        if (model.Amount.HasValue && !string.IsNullOrEmpty(model.CurrencyCode))
            return $"Transaction of {model.Amount:F2} {model.CurrencyCode}";
        return "Transaction notification";
    }
}
