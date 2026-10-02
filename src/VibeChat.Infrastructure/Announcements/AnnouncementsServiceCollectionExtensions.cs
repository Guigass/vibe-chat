using Microsoft.Extensions.DependencyInjection;

namespace VibeChat.Infrastructure;

internal static class AnnouncementsServiceCollectionExtensions
{
    internal static IServiceCollection AddAnnouncementsInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IAnnouncementWriter, AnnouncementWriter>();
        return services;
    }
}
