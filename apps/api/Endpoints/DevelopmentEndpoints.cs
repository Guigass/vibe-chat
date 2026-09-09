using Microsoft.EntityFrameworkCore;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class DevelopmentEndpoints
{
    internal static void MapDevelopment(this RouteGroupBuilder v1, WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            v1.MapPost("/dev/seed", async (IServiceProvider services, IConfiguration config, IClock clock, ILoggerFactory logs, CancellationToken ct) =>
            {
                // Seed must use migrator/BYPASSRLS — never the request-path app role (SEC-RLS-RUNTIME).
                var migratorCs = DatabaseBootstrap.ResolveMigratorConnectionString(config);
                var options = new DbContextOptionsBuilder<VibeChatDbContext>().UseNpgsql(migratorCs).Options;
                await using var seedDb = new VibeChatDbContext(options, new TenantContext());
                var seed = new SeedData(seedDb, clock, logs.CreateLogger<SeedData>());
                await seed.SeedAsync(ct);
                return Results.Ok(new { seeded = true });
            }).AllowAnonymous().AllowPermissionGateExempt("Development seed; AllowAnonymous lab-only");
        }
    }
}
