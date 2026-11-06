using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headroom.Client.Api;

public static class ApiClientExtensions
{
    // Registers a typed HttpClient for the same-origin API. It sends the antiforgery header on writes.
    // Every screen adds its own client: services.AddApiClient<PeopleApi>(builder.HostEnvironment.BaseAddress).
    public static IHttpClientBuilder AddApiClient<TClient>(this IServiceCollection services, string baseAddress)
        where TClient : class
    {
        services.TryAddSingleton<AntiforgeryToken>();
        services.TryAddTransient<AntiforgeryHandler>();
        return services.AddHttpClient<TClient>(c => c.BaseAddress = new Uri(baseAddress))
            .AddHttpMessageHandler<AntiforgeryHandler>();
    }
}
