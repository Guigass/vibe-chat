using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VibeChat.Realtime;

namespace VibeChat.Infrastructure;

internal static class LiveServiceCollectionExtensions
{
    internal static IServiceCollection AddLiveInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LiveOptions>(configuration.GetSection(LiveOptions.SectionName));
        services.Configure<MeetingAiOptions>(configuration.GetSection(MeetingAiOptions.SectionName));
        services.AddHttpClient(LiveRules.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(2);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<ILiveMediaProvider, LiveMediaProvider>();
        services.AddScoped<ILiveRecordingSink, LiveRecordingSink>();
        services.AddSingleton<LiveTranscriptionProbe>();
        services.AddSingleton<LocalLiveTranscriptionProvider>();
        services.AddSingleton<ExternalLiveTranscriptionProvider>();
        services.AddSingleton<NullLiveTranscriptionProvider>();
        services.AddSingleton<ILiveTranscriptionProvider>(sp =>
            MeetingAiRules.NormalizeProvider(sp.GetRequiredService<IOptions<MeetingAiOptions>>().Value.Provider) switch
            {
                MeetingAiRules.ProviderLocal => sp.GetRequiredService<LocalLiveTranscriptionProvider>(),
                MeetingAiRules.ProviderExternal => sp.GetRequiredService<ExternalLiveTranscriptionProvider>(),
                _ => sp.GetRequiredService<NullLiveTranscriptionProvider>()
            });
        services.AddScoped<LiveTranscriptionJob>();
        return services;
    }
}
