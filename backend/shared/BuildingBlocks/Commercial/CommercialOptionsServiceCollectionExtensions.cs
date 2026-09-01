using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Commercial;

public static class CommercialOptionsServiceCollectionExtensions
{
    public static OptionsBuilder<CommercialOptions> AddCommercialOptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IValidateOptions<CommercialOptions>, CommercialOptionsValidator>();

        return services
            .AddOptions<CommercialOptions>()
            .BindConfiguration(CommercialOptions.Section)
            .ValidateOnStart();
    }
}


