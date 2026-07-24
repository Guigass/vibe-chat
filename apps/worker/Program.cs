using VibeChat.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddVibeChatInfrastructure(builder.Configuration, useSignalRPublisher: false);

var host = builder.Build();
host.Run();
