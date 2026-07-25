using VibeChat.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddVibeChatInfrastructure(builder.Configuration, useSignalRPublisher: false);
// B-047: purge loop only on worker (not API) — ADR-018 hard-delete behind MessageRetention:Enabled.
builder.Services.AddHostedService<MessageRetentionPurgeDispatcher>();

var host = builder.Build();
host.Run();
