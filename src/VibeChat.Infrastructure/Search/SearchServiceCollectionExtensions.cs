using Microsoft.Extensions.DependencyInjection;
using VibeChat.Search;

namespace VibeChat.Infrastructure;

internal static class SearchServiceCollectionExtensions
{
    internal static IServiceCollection AddSearchInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ISearchIndexer, PostgresSearchIndexer>();
        services.AddScoped<ISearchQuery, PostgresSearchQuery>();
        return services;
    }
}
