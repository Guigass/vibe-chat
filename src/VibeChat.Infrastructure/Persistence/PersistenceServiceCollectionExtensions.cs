using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure;

internal static class PersistenceServiceCollectionExtensions
{
    internal static IServiceCollection AddPersistenceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<RlsConnectionInterceptor>();
        services.AddDbContext<VibeChatDbContext>((sp, options) =>
        {
            options.UseNpgsql(DatabaseBootstrap.ResolveRuntimeConnectionString(configuration));
            options.AddInterceptors(sp.GetRequiredService<RlsConnectionInterceptor>());
        });
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();
        services.AddScoped<IAuditWriter, EfAuditWriter>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped<IConversationSequenceStore, ConversationSequenceStore>();
        return services;
    }

    internal static IServiceCollection AddAccessInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<PermissionChecker>();
        services.AddScoped<IPermissionChecker>(sp => sp.GetRequiredService<PermissionChecker>());
        services.AddScoped<IWorkspaceMembershipReader>(sp => sp.GetRequiredService<PermissionChecker>());
        services.AddScoped<IChannelMembershipReader>(sp => sp.GetRequiredService<PermissionChecker>());
        services.AddScoped<IDashboardQuery, DashboardQuery>();
        return services;
    }

    internal static IServiceCollection AddWorkspaceSeedInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<SeedData>();
        return services;
    }
}
