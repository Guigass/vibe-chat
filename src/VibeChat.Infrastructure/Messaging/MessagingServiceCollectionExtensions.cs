using Microsoft.Extensions.DependencyInjection;
using VibeChat.Messaging;

namespace VibeChat.Infrastructure;

internal static class MessagingServiceCollectionExtensions
{
    internal static IServiceCollection AddMessagingInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IMessageWriter, MessageWriter>();
        return services;
    }
}
