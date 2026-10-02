using Microsoft.Extensions.DependencyInjection;

namespace VibeChat.Infrastructure;

internal static class SchedulingServiceCollectionExtensions
{
    internal static IServiceCollection AddSchedulingInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ScheduleDispatchProcessor>();
        return services;
    }
}
