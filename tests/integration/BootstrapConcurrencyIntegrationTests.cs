using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>
/// Several API hosts boot against one database (collection host plus WithWebHostBuilder).
/// Each calls <see cref="DatabaseBootstrap.MigrateSeedAndProtectAsync"/>.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class BootstrapConcurrencyIntegrationTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Parallel_bootstrap_finishes_without_deadlock()
    {
        using var client = factory.CreateClient();
        var services = factory.Services;
        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();

        var tasks = Enumerable.Range(0, 4)
            .Select(_ => DatabaseBootstrap.MigrateSeedAndProtectAsync(
                services,
                configuration,
                environment,
                CancellationToken.None))
            .ToArray();

        var completed = Task.WhenAll(tasks);
        var finished = await Task.WhenAny(completed, Task.Delay(TimeSpan.FromMinutes(3)));
        finished.Should().Be(completed, "parallel bootstrap must not wait forever on a Postgres deadlock");
        await completed;
    }
}
