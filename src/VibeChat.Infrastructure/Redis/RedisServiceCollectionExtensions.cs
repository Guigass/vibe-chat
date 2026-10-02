using Microsoft.Extensions.DependencyInjection;
using VibeChat.Realtime;

namespace VibeChat.Infrastructure;

internal static class RedisServiceCollectionExtensions
{
    internal static IServiceCollection AddRedisInfrastructure(this IServiceCollection services, bool useSignalRPublisher)
    {
        services.AddSingleton<RedisConnection>();
        services.AddScoped<ITypingService, TypingService>();
        services.AddScoped<IPresenceService, PresenceService>();
        if (useSignalRPublisher)
        {
            services.AddScoped<IChatPublisher, SignalRChatPublisher>();
            services.AddHostedService<RedisSignalRBridge>();
        }
        else
        {
            services.AddScoped<IChatPublisher, RedisChannelChatPublisher>();
        }

        return services;
    }
}
