using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.Infrastructure.Compliance;
using VibeChat.Integrations;

namespace VibeChat.Infrastructure;

internal static class IntegrationsServiceCollectionExtensions
{
    internal static IServiceCollection AddIntegrationsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EventSchemaOptions>(configuration.GetSection(EventSchemaOptions.SectionName));
        services.Configure<AutomationOptions>(configuration.GetSection(AutomationOptions.SectionName));
        services.Configure<ConnectorOptions>(configuration.GetSection(ConnectorOptions.SectionName));
        services.AddScoped<AutomationService>();
        services.AddScoped<ConnectorService>();
        services.AddSingleton<IConnectorAddressLookup, DnsConnectorAddressLookup>();
        services.AddSingleton<IConnectorTransport, HttpConnectorTransport>();
        // B-048: outbound webhooks — tenant URL+HMAC secret; best-effort after MessageCreated outbox.
        services.AddHttpClient(OutboundWebhookDispatcher.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "VibeChat-Webhooks/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddScoped<IOutboundWebhookDispatcher, OutboundWebhookDispatcher>();
        services.Configure<DeveloperPortalOptions>(configuration.GetSection(DeveloperPortalOptions.SectionName));
        services.AddScoped<DeveloperPortalService>();
        services.Configure<PluginPlatformOptions>(configuration.GetSection(PluginPlatformOptions.SectionName));
        services.AddHttpClient(PluginCallbackDispatcher.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "VibeChat-Plugins/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddScoped<IPluginCallbackDispatcher, PluginCallbackDispatcher>();
        services.AddHttpClient(SiemExportService.HttpClientName, client =>
        {
            client.Timeout = SiemLimits.HttpTimeout;
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "VibeChat-SIEM/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddHttpClient(HttpConnectorTransport.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "VibeChat-Connectors/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        return services;
    }
}
