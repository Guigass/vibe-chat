using Microsoft.Extensions.DependencyInjection;
using VibeChat.Messaging;

namespace VibeChat.Infrastructure;

internal static class PollsServiceCollectionExtensions
{
    internal static IServiceCollection AddPollsInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IPollWriter, PollWriter>();
        return services;
    }

    // Registered after announcements so the previous service order stays intact.
    internal static IServiceCollection AddPollCloseInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<PollCloseProcessor>();
        return services;
    }
}
