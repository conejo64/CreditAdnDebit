namespace BuildingBlocks.Commercial;

public sealed class CommercialOptions
{
    public const string Section = "Commercial";

    public CommercialMode Mode { get; set; } = CommercialMode.Commercial;

    public bool EnableDemoSurfaces { get; set; }

    public bool EnableAnonymousDiagnostics { get; set; }

    public bool EnableSwagger { get; set; }

    public string ClaimRegisterVersion { get; set; } = "unpublished";

    public bool IsCommercialMode => Mode == CommercialMode.Commercial;

    public bool CanExposeDemoSurfaces => Mode == CommercialMode.Demo && EnableDemoSurfaces;

    public bool CanExposeAnonymousDiagnostics => Mode == CommercialMode.Demo && EnableAnonymousDiagnostics;

    public bool CanExposeSwagger => Mode == CommercialMode.Demo && EnableSwagger;
}
