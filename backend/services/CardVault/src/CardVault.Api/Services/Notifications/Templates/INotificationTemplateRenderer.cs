namespace CardVault.Application.Services.Notifications.Templates;

/// <summary>
/// Renders a notification template into a subject + body pair.
/// <para>
/// Implementations MUST call the <see cref="PciTemplateGuard"/> before rendering and
/// MUST NOT log the rendered body.
/// </para>
/// </summary>
public interface INotificationTemplateRenderer
{
    /// <summary>
    /// Renders the template identified by <see cref="TemplateModel.TemplateType"/> and
    /// <see cref="TemplateModel.Locale"/>, applying locale fallback (null/unsupported → es-EC).
    /// </summary>
    /// <param name="model">PCI-safe model. Guard runs before render.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rendered subject and body.</returns>
    /// <exception cref="PciTemplateViolationException">
    /// If the model contains raw PAN, OTP seed, or other prohibited data.
    /// </exception>
    Task<RenderedTemplate> RenderAsync(TemplateModel model, CancellationToken ct = default);
}
