using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.Messaging;

namespace VibeChat.Infrastructure;

internal static class RuntimeSettingsServiceCollectionExtensions
{
    internal static IServiceCollection AddRuntimeSettingsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AvailabilityCalendarOptions>(configuration.GetSection(AvailabilityCalendarOptions.SectionName));
        services.Configure<GroupDmOptions>(configuration.GetSection(GroupDmOptions.SectionName));
        services.Configure<InviteOptions>(configuration.GetSection(InviteOptions.SectionName));
        services.Configure<BotIntegrationOptions>(configuration.GetSection(BotIntegrationOptions.SectionName));
        services.Configure<MessageRetentionOptions>(configuration.GetSection(MessageRetentionOptions.SectionName));
        services.Configure<RuntimeSettingsOptions>(configuration.GetSection(RuntimeSettingsOptions.SectionName));
        services.Configure<MessageHistoryOptions>(configuration.GetSection(MessageHistoryOptions.SectionName));
        services.AddMemoryCache();
        services.AddSingleton<RuntimeSecretProtector>();
        services.AddSingleton<IRuntimeSettingsCacheInvalidator, RuntimeSettingsCacheInvalidator>();
        services.AddScoped<ProcessSettingsResolver>();
        services.AddScoped<AiSettingsResolver>();
        services.AddScoped<FilesSettingsResolver>();
        services.AddScoped<RateLimitSettingsResolver>();
        services.AddScoped<WebhookEndpointResolver>();
        services.AddScoped<WebhookAdminService>();
        services.AddScoped<RuntimeSettingsAdminService>();
        return services;
    }
}
