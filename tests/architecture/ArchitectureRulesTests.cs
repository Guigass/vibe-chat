using FluentAssertions;
using NetArchTest.Rules;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Messaging;
using VibeChat.Search;
using VibeChat.Tenancy;

namespace VibeChat.ArchitectureTests;

public sealed class ArchitectureRulesTests
{
    [Fact]
    public void Messaging_module_does_not_reference_administration_internals()
    {
        var result = Types.InAssembly(typeof(Message).Assembly)
            .ShouldNot()
            .HaveDependencyOn("VibeChat.Administration")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Core_domain_modules_do_not_reference_infrastructure_or_api()
    {
        var assemblies = new[]
        {
            typeof(Message).Assembly,
            typeof(Channel).Assembly,
            typeof(Workspace).Assembly,
            typeof(UserProfile).Assembly,
            typeof(ISearchQuery).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny("VibeChat.Infrastructure", "VibeChat.Api")
                .GetResult();

            result.IsSuccessful.Should().BeTrue();
        }
    }
}
