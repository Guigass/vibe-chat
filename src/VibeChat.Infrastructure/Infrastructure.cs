using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VibeChat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddVibeChatInfrastructure(this IServiceCollection services, IConfiguration configuration, bool useSignalRPublisher = true)
    {
        services.AddPersistenceInfrastructure(configuration);
        services.AddMessagingInfrastructure();
        services.AddPollsInfrastructure();
        services.AddAnnouncementsInfrastructure();
        services.AddPollCloseInfrastructure();
        services.AddSchedulingInfrastructure();
        services.AddSearchInfrastructure();
        services.AddRateLimitingInfrastructure();
        services.AddAccessInfrastructure();
        services.AddAiFeaturesInfrastructure();
        services.AddRedisInfrastructure(useSignalRPublisher);
        services.AddOutboxInfrastructure();
        services.AddRuntimeSettingsInfrastructure(configuration);
        services.AddMessageRetentionInfrastructure();
        services.AddWorkspaceSeedInfrastructure();
        services.AddFilesInfrastructure();
        services.AddAiProvidersInfrastructure();
        services.AddEmailInfrastructure();
        services.AddPushInfrastructure();
        services.AddIntegrationsInfrastructure();
        services.AddInfrastructureHealthChecks();
        return services;
    }
}
