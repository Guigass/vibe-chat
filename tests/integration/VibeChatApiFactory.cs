using VibeChat.TestHost;
using Xunit;

namespace VibeChat.IntegrationTests;

// Re-export shared factory collection for this assembly.
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<VibeChatApiFactory>
{
    public const string Name = VibeChatApiCollection.Name;
}
