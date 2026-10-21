using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class CapacityEndpoints
{
    internal static void MapCapacity(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/capacity", DashboardAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/capacity/evaluations", EvaluateAsync)
            .RequirePermission(Permissions.Workspace.Admin);
    }

    private static IResult DashboardAsync(CapacitySloService capacity) => Result(capacity.Dashboard());

    private static async Task<IResult> EvaluateAsync(HttpRequest request, CapacitySloService capacity, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync(ct);
        return Result(capacity.Evaluate(json));
    }

    private static IResult Result(CapacitySloResult result) =>
        result.Error is null
            ? Results.Ok(result.Payload)
            : Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
}
