using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Integrations;

namespace VibeChat.Infrastructure;

internal static class IntegrationsServiceCollectionExtensions
{
    internal static IServiceCollection AddIntegrationsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AutomationOptions>(configuration.GetSection(AutomationOptions.SectionName));
        services.AddScoped<AutomationService>();
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
        return services;
    }
}
