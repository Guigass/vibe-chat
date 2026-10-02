using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.AI;

namespace VibeChat.Infrastructure;

internal static class AiServiceCollectionExtensions
{
    internal static IServiceCollection AddAiFeaturesInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ISummarizeChannelFeature, SummarizeChannelFeature>();
        services.AddScoped<ISuggestChannelReplyFeature, SuggestChannelReplyFeature>();
        services.AddScoped<ITranscribeAttachmentFeature, TranscribeAttachmentFeature>();
        return services;
    }

    internal static IServiceCollection AddAiProvidersInfrastructure(this IServiceCollection services)
    {
        // ADR-020: never capture API key in DefaultRequestHeaders — set per HttpRequestMessage.
        services.AddHttpClient<OpenRouterAiProvider>((_, client) =>
        {
            client.BaseAddress = new Uri(ProcessSettingsDefaults.OpenRouterBaseUrl);
        });

        services.AddScoped<IAiCompletionProvider>(_ => new MockAiProvider());
        return services;
    }
}
