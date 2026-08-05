using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VibeChat.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddVibeChatInfrastructure(builder.Configuration, useSignalRPublisher: false);
// B-047: purge loop only on worker (not API) — ADR-018 hard-delete behind MessageRetention:Enabled.
builder.Services.AddHostedService<MessageRetentionPurgeDispatcher>();

var host = builder.Build();

// SEC-RLS-RUNTIME: worker must use the non-privileged app role (migrations stay on API/migrator).
await DatabaseBootstrap.ValidateRuntimeRoleAsync(
    DatabaseBootstrap.ResolveRuntimeConnectionString(builder.Configuration),
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseBootstrap"),
    CancellationToken.None);

host.Run();
