using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using VibeChat.Api;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class HaEndpoints
{
    internal static void MapHa(this RouteGroupBuilder v1)
    {
        v1.MapGet("/meta/runtime", RuntimeAsync)
            .RequirePermission(Permissions.Support.Read);
        v1.MapPost("/admin/ha/drain", DrainAsync)
            .RequirePermission(Permissions.Support.Repair);
    }

    private static IResult RuntimeAsync(IOptions<HaOptions> options, HaAdmission admission, HaJobGate jobs)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        return Results.Ok(HaRuntime.Describe(options.Value, admission, jobs));
    }

    private static async Task<IResult> DrainAsync(
        IOptions<HaOptions> options,
        HaAdmission admission,
        IHubContext<ChatHub> hub,
        ILogger<HaDrainLog> logger,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        foreach (var connectionId in admission.ConnectionIds)
        {
            await hub.Clients.Client(connectionId).SendAsync(
                "NodeDraining",
                new { retry = true },
                cancellationToken);
        }

        admission.BeginDrain();
        logger.LogInformation("HA drain started on node {NodeId}", admission.NodeId);

        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, options.Value.DrainDeadlineSeconds));
        while (admission.InFlight > 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, cancellationToken);
        }

        return Results.Ok(new { draining = true });
    }

    private static IResult Disabled() =>
        Results.Json(new { error = "HaDisabled" }, statusCode: StatusCodes.Status404NotFound);
}

internal sealed class HaDrainLog;
