using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Notifications;

namespace VibeChat.Infrastructure;

internal static class PushServiceCollectionExtensions
{
    internal static IServiceCollection AddPushInfrastructure(this IServiceCollection services)
    {
        // B-095 / ADR-022: Web Push off by default; recording sender for tests.
        services.AddSingleton<RecordingPushSender>();
        services.AddHttpClient(PushOptions.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "VibeChat-WebPush/1.0");
        });
        services.AddScoped<WebPushSender>();
        services.AddScoped<IPushSender>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            if (cfg.GetValue("Push:UseRecordingSender", false))
            {
                return sp.GetRequiredService<RecordingPushSender>();
            }

            return sp.GetRequiredService<WebPushSender>();
        });
        services.AddScoped<PushDispatcher>();
        return services;
    }
}
