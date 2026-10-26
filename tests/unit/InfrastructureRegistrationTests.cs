using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Infrastructure;

namespace VibeChat.UnitTests;

public sealed class InfrastructureRegistrationTests
{
    [Fact]
    public void AddVibeChatInfrastructure_preserves_application_service_order()
    {
        var signalR = Describe(useSignalRPublisher: true);
        var worker = Describe(useSignalRPublisher: false);

        signalR.Should().Equal(
            "Singleton:IClock:SystemClock",
            "Scoped:ITenantContext:TenantContext",
            "Scoped:RlsConnectionInterceptor:RlsConnectionInterceptor",
            "Scoped:VibeChatDbContext:VibeChatDbContext",
            "Scoped:IOutboxWriter:EfOutboxWriter",
            "Scoped:IAuditWriter:EfAuditWriter",
            "Scoped:IIdempotencyStore:EfIdempotencyStore",
            "Scoped:IConversationSequenceStore:ConversationSequenceStore",
            "Scoped:IExternalSessionRevoker:NoopExternalSessionRevoker",
            "Scoped:IProvisioningPort:ScimProvisioningService",
            "Scoped:IMessageWriter:MessageWriter",
            "Scoped:IPollWriter:PollWriter",
            "Scoped:IAnnouncementWriter:AnnouncementWriter",
            "Singleton:PollCloseProcessor:PollCloseProcessor",
            "Singleton:TaskOverdueProcessor:TaskOverdueProcessor",
            "Singleton:ScheduleDispatchProcessor:ScheduleDispatchProcessor",
            "Scoped:ISearchIndexer:PostgresSearchIndexer",
            "Scoped:ISearchQuery:PostgresSearchQuery",
            "Singleton:QdrantSemanticIndex:QdrantSemanticIndex",
            "Scoped:SemanticSearchService:SemanticSearchService",
            "Scoped:ISemanticIndex:factory",
            "Scoped:ISemanticQuery:factory",
            "Singleton:IRateLimiter:RedisRateLimiter",
            "Scoped:PermissionChecker:PermissionChecker",
            "Scoped:IPermissionChecker:factory",
            "Scoped:IWorkspaceMembershipReader:factory",
            "Scoped:IChannelMembershipReader:factory",
            "Scoped:IDashboardQuery:DashboardQuery",
            "Scoped:ISummarizeChannelFeature:SummarizeChannelFeature",
            "Scoped:ISuggestChannelReplyFeature:SuggestChannelReplyFeature",
            "Scoped:ITranscribeAttachmentFeature:TranscribeAttachmentFeature",
            "Singleton:RedisConnection:RedisConnection",
            "Scoped:ITypingService:TypingService",
            "Scoped:IPresenceService:PresenceService",
            "Scoped:IChatPublisher:SignalRChatPublisher",
            "Singleton:IHostedService:RedisSignalRBridge",
            "Singleton:OutboxProcessor:OutboxProcessor",
            "Singleton:IHostedService:OutboxDispatcher",
            "Scoped:WorkspaceImportService:WorkspaceImportService",
            "Scoped:SupportDiagnosticsService:SupportDiagnosticsService",
            "Scoped:LegalHoldService:LegalHoldService",
            "Scoped:DlpService:DlpService",
            "Scoped:SiemExportService:SiemExportService",
            "Scoped:PolicyPackService:PolicyPackService",
            "Scoped:DelegatedAdminService:DelegatedAdminService",
            "Scoped:CapacitySloService:CapacitySloService",
            "Scoped:StorageLifecycleService:StorageLifecycleService",
            "Scoped:ChatBackupTransfer:ChatBackupTransfer",
            "Scoped:ChatBackupService:ChatBackupService",
            "Singleton:ChatBackupDispatchProcessor:ChatBackupDispatchProcessor",
            "Singleton:RuntimeSecretProtector:RuntimeSecretProtector",
            "Singleton:IRuntimeSettingsCacheInvalidator:RuntimeSettingsCacheInvalidator",
            "Scoped:ProcessSettingsResolver:ProcessSettingsResolver",
            "Scoped:AiSettingsResolver:AiSettingsResolver",
            "Scoped:FilesSettingsResolver:FilesSettingsResolver",
            "Scoped:RateLimitSettingsResolver:RateLimitSettingsResolver",
            "Scoped:WebhookEndpointResolver:WebhookEndpointResolver",
            "Scoped:WebhookAdminService:WebhookAdminService",
            "Scoped:RuntimeSettingsAdminService:RuntimeSettingsAdminService",
            "Singleton:MessageRetentionPurgeProcessor:MessageRetentionPurgeProcessor",
            "Scoped:SeedData:SeedData",
            "Singleton:IMinioClient:factory",
            "Singleton:MinioPresignClient:factory",
            "Scoped:IObjectStorage:MinioObjectStorage",
            "Scoped:IMalwareScanner:factory",
            "Scoped:AttachmentThumbnailGenerator:AttachmentThumbnailGenerator",
            "Scoped:LinkPreviewSettingsResolver:LinkPreviewSettingsResolver",
            "Scoped:LinkPreviewFetcher:LinkPreviewFetcher",
            "Scoped:LinkPreviewGenerator:LinkPreviewGenerator",
            "Transient:OpenRouterAiProvider:factory",
            "Scoped:IAiCompletionProvider:factory",
            "Scoped:EmailSettingsResolver:EmailSettingsResolver",
            "Scoped:IEmailSender:SmtpEmailSender",
            "Singleton:RecordingPushSender:RecordingPushSender",
            "Scoped:WebPushSender:WebPushSender",
            "Scoped:IPushSender:factory",
            "Scoped:PushDispatcher:PushDispatcher",
            "Scoped:DigestGenerator:DigestGenerator",
            "Singleton:DigestDispatchProcessor:DigestDispatchProcessor",
            "Scoped:AutomationService:AutomationService",
            "Scoped:ConnectorService:ConnectorService",
            "Singleton:IConnectorAddressLookup:DnsConnectorAddressLookup",
            "Singleton:IConnectorTransport:HttpConnectorTransport",
            "Scoped:IOutboundWebhookDispatcher:OutboundWebhookDispatcher",
            "Scoped:DeveloperPortalService:DeveloperPortalService",
            "Scoped:IPluginCallbackDispatcher:PluginCallbackDispatcher",
            "Scoped:PluginSdkService:PluginSdkService",
            "Scoped:BrandingService:BrandingService",
            "Scoped:DesktopClientService:DesktopClientService",
            "Scoped:MobileClientService:MobileClientService");

        worker.Should().Equal(signalR.Select(entry => entry
            .Replace("Scoped:IChatPublisher:SignalRChatPublisher", "Scoped:IChatPublisher:RedisChannelChatPublisher", StringComparison.Ordinal)
            .Replace("Singleton:IHostedService:RedisSignalRBridge", "", StringComparison.Ordinal))
            .Where(entry => entry.Length > 0));
    }

    [Fact]
    public void AddVibeChatInfrastructure_preserves_options_binding_order()
    {
        var services = new ServiceCollection();
        services.AddVibeChatInfrastructure(new ConfigurationBuilder().Build());

        var optionTypes = services
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.IsGenericType && type.Name.StartsWith("IConfigureOptions", StringComparison.Ordinal))
            .Select(type => type.GetGenericArguments()[0].Name)
            .ToArray();

        optionTypes.Should().StartWith(
            "AvailabilityCalendarOptions",
            "GroupDmOptions",
            "InviteOptions",
            "BotIntegrationOptions",
            "MessageRetentionOptions",
            "RuntimeSettingsOptions");

        services.Count(descriptor =>
                descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericArguments()[0].Name == "HealthCheckServiceOptions")
            .Should().Be(3);
    }

    private static IReadOnlyList<string> Describe(bool useSignalRPublisher)
    {
        var services = new ServiceCollection();
        services.AddVibeChatInfrastructure(new ConfigurationBuilder().Build(), useSignalRPublisher);
        return services
            .Where(IsApplicationDescriptor)
            .Select(descriptor =>
            {
                var implementation = descriptor.ImplementationType?.Name ?? "factory";
                return $"{descriptor.Lifetime}:{descriptor.ServiceType.Name}:{implementation}";
            })
            .ToArray();
    }

    private static bool IsApplicationDescriptor(ServiceDescriptor descriptor)
    {
        if (IsApplicationType(descriptor.ServiceType))
        {
            return true;
        }

        return descriptor.ImplementationType is not null && IsApplicationType(descriptor.ImplementationType);
    }

    private static bool IsApplicationType(Type type)
    {
        var ns = type.Namespace;
        return ns is not null && (ns.StartsWith("VibeChat.", StringComparison.Ordinal) || ns.StartsWith("Minio", StringComparison.Ordinal));
    }
}
