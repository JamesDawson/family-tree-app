using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Data.ExternalSources.Http;

public static class ExternalSourceHttpClientExtensions
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers a named <see cref="HttpClient"/> for an API-backed source: base address, a short timeout and a
    /// descriptive User-Agent (several of these services ask API clients to identify themselves).
    /// </summary>
    public static IHttpClientBuilder AddExternalSourceHttpClient(this IServiceCollection services, string name, string baseAddress) =>
        services.AddHttpClient(name, client =>
        {
            client.BaseAddress = new Uri(baseAddress);
            client.Timeout = Timeout;
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("FamilyTree", typeof(ExternalSourceHttpClientExtensions).Assembly.GetName().Version?.ToString(3) ?? "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
}
