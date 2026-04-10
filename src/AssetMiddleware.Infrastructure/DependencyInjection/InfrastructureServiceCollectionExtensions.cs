using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Infrastructure.Caching;
using AssetMiddleware.Infrastructure.Http;
using AssetMiddleware.Infrastructure.Resilience;
using AssetMiddleware.Infrastructure.ServiceBus;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AssetMiddleware.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Options ────────────────────────────────────────────────
        services.Configure<AssetHubOptions>(
            configuration.GetSection(AssetHubOptions.SectionName));
        services.Configure<ServiceBusOptions>(
            configuration.GetSection(ServiceBusOptions.SectionName));

        // ── Service Bus ───────────────────────────────────────────
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;

            // Local dev: use connection string (emulator)
            // Production: use fully qualified namespace + Managed Identity
            if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
                return new ServiceBusClient(opts.ConnectionString);

            return new ServiceBusClient(opts.FullyQualifiedNamespace, new DefaultAzureCredential());
        });

        services.AddHostedService<ServiceBusEventSubscriber>();
        services.AddSingleton<IDeadLetterQueueProcessor, DeadLetterQueueProcessor>();

        // ── Token provider — uses a dedicated client (no auth handler loop) ──
        services.AddHttpClient("AssetHub.Token", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<AssetHubOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
        });

        services.AddSingleton<ITokenProvider, OAuthTokenProvider>();
        services.AddSingleton(TimeProvider.System);

        // ── Named client for image download (external URLs, no auth headers) ──
        services.AddHttpClient("ImageDownload");

        // ── Typed AssetHub client — WITH auth delegating handler + resilience ──
        services.AddTransient<MetricsDelegatingHandler>();
        services.AddTransient<OAuthDelegatingHandler>();
        services.AddTransient<RateLimitDelegatingHandler>();

        services.AddHttpClient<IAssetHubClient, AssetHubHttpClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<AssetHubOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
        })
        .AddHttpMessageHandler<MetricsDelegatingHandler>()
        .AddHttpMessageHandler<OAuthDelegatingHandler>()
        .AddHttpMessageHandler<RateLimitDelegatingHandler>()
        .AddAssetHubResilience(configuration);

        // ── Circuit breaker state tracker — singleton ──────────────
        services.AddSingleton<CircuitBreakerStateTracker>();

        // ── Status cache (fetches Active statusId once on first call) ──
        services.AddSingleton<IAssetStatusCache, AssetStatusCache>();

        return services;
    }
}
