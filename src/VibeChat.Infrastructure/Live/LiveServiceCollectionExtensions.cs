using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Realtime;

namespace VibeChat.Infrastructure;

internal static class LiveServiceCollectionExtensions
{
    internal static IServiceCollection AddLiveInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LiveOptions>(configuration.GetSection(LiveOptions.SectionName));
        services.AddHttpClient(LiveRules.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(2);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<ILiveMediaProvider, LiveMediaProvider>();
        return services;
    }
}
