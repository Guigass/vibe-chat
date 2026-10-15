using Microsoft.Extensions.DependencyInjection;
using VibeChat.Search;

namespace VibeChat.Infrastructure;

internal static class SearchServiceCollectionExtensions
{
    internal static IServiceCollection AddSearchInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ISearchIndexer, PostgresSearchIndexer>();
        services.AddScoped<ISearchQuery, PostgresSearchQuery>();
        services.AddSingleton<QdrantSemanticIndex>();
        services.AddScoped<SemanticSearchService>();
        services.AddScoped<ISemanticIndex>(sp => sp.GetRequiredService<SemanticSearchService>());
        services.AddScoped<ISemanticQuery>(sp => sp.GetRequiredService<SemanticSearchService>());
        return services;
    }
}
