using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace VibeChat.SecurityTests;

public sealed class VibeChatApiFactory : WebApplicationFactory<Program>
{
    public const string DatabaseConnection =
        "Host=localhost;Port=5432;Database=vibechat_test;Username=vibechat;Password=vibechat_dev_password_change_me";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = DatabaseConnection,
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["Minio:Endpoint"] = "localhost:9000",
                ["Minio:AccessKey"] = "minioadmin",
                ["Minio:SecretKey"] = "minioadmin_dev_password_change_me",
                ["Minio:Bucket"] = "vibechat",
                ["Minio:UseSsl"] = "false",
                ["Seed:Enabled"] = "true",
                ["Ai:Enabled"] = "true",
                ["Ai:Provider"] = "Mock",
                ["Authentication:RequireHttpsMetadata"] = "false"
            });
        });
    }
}

[CollectionDefinition(Name)]
public sealed class SecurityCollection : ICollectionFixture<VibeChatApiFactory>
{
    public const string Name = "security";
}
