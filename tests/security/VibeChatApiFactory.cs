using VibeChat.TestHost;
using Xunit;

namespace VibeChat.SecurityTests;

// Re-export shared factory collection for this assembly.
[CollectionDefinition(Name)]
public sealed class SecurityCollection : ICollectionFixture<VibeChatApiFactory>
{
    public const string Name = VibeChatApiCollection.Name;
}
