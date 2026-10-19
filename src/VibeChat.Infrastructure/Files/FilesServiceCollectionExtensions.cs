using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using VibeChat.Files;
using VibeChat.Messaging;

namespace VibeChat.Infrastructure;

internal static class FilesServiceCollectionExtensions
{
    internal static IServiceCollection AddFilesInfrastructure(this IServiceCollection services)
    {
        // Resolve MinIO from IConfiguration at runtime so WebApplicationFactory overrides apply.
        // Internal client (Endpoint) for health/Stat; presign client (PublicEndpoint) for browser URLs.
        services.AddSingleton<IMinioClient>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            return MinioEndpoint.CreateClient(cfg, MinioEndpoint.ResolveInternalEndpoint(cfg));
        });
        services.AddSingleton(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var client = MinioEndpoint.CreateClient(cfg, MinioEndpoint.ResolvePublicEndpoint(cfg));
            return new MinioPresignClient(client);
        });
        services.AddScoped<IObjectStorage, MinioObjectStorage>();
        services.AddScoped<IMalwareScanner>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MalwareScanOptions>>().Value;
            var timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 30));
            if (!options.Enabled)
            {
                return new NullMalwareScanner();
            }

            if (string.Equals(options.Engine, "clamav", StringComparison.OrdinalIgnoreCase))
            {
                return new ClamAvMalwareScanner(options.ClamAvEndpoint, timeout);
            }

            return new PolicyMalwareScanner(timeout);
        });
        services.AddScoped<AttachmentThumbnailGenerator>();
        services.AddScoped<LinkPreviewSettingsResolver>();
        services.AddScoped<LinkPreviewFetcher>();
        services.AddScoped<LinkPreviewGenerator>();
        services.AddHttpClient(LinkPreviewFetcher.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(LinkPreviewHttpHandlerFactory.Create)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", LinkPreviewPolicies.UserAgent);
            });
        return services;
    }
}
