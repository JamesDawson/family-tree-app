using FamilyTree.Data.ExternalSources.Http;
using FamilyTree.Data.ExternalSources.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FamilyTree.Data.ExternalSources;

public static class ExternalSourcesServiceCollectionExtensions
{
    /// <summary>Registers the source registry and the built-in sources. Add further sources with <c>TryAddEnumerable</c>.</summary>
    public static IServiceCollection AddExternalDataSources(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExternalSourcesOptions>(configuration.GetSection(ExternalSourcesOptions.SectionName));
        services.Configure<WikiTreeOptions>(configuration.GetSection(WikiTreeOptions.SectionName));
        services.AddSingleton<IExternalDataSourceRegistry, ExternalDataSourceRegistry>();

        // Link-out sources: no server-side requests.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalDataSource, NationalArchivesIrelandCensusSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalDataSource, FreeCenSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalDataSource, FreeBmdSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalDataSource, FamilySearchSource>());

        // API sources.
        services.AddExternalSourceHttpClient(WikiTreeSource.ClientName, WikiTreeSource.BaseAddress);
        services.AddExternalSourceHttpClient(NationalArchivesDiscoverySource.ClientName, NationalArchivesDiscoverySource.BaseAddress);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalDataSource, WikiTreeSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalDataSource, NationalArchivesDiscoverySource>());

        return services;
    }
}
