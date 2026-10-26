using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class ChatBackupEndpoints
{
    internal static void MapChatBackup(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/chat-backup", ListAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/chat-backup/destinations", CreateDestinationAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/chat-backup/destinations/{destinationId:guid}/test", TestAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/chat-backup/destinations/{destinationId:guid}/rotate", RotateAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapDelete("/workspaces/{workspaceId:guid}/chat-backup/destinations/{destinationId:guid}", DeleteAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/chat-backup/jobs", CreateJobAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPatch("/workspaces/{workspaceId:guid}/chat-backup/jobs/{jobId:guid}", UpdateJobAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/chat-backup/jobs/{jobId:guid}/run", RunAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapGet("/workspaces/{workspaceId:guid}/chat-backup/export", ExportAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/chat-backup/import/validate", ValidateAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/chat-backup/import/execute", ExecuteAsync)
            .RequirePermission(Permissions.Workspace.Admin);
    }

    private static async Task<IResult> ListAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, ChatBackupService backup, CancellationToken ct)
    {
        var workspace = await OpenAsync(workspaceId, http, db, tenant, clock, ct);
        return workspace is null ? Results.Forbid() : Result(await backup.ListAsync(workspace, ct));
    }

    private static async Task<IResult> CreateDestinationAsync(Guid workspaceId, ChatBackupDestinationRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, ChatBackupService backup, CancellationToken ct)
    {
        var opened = await OpenActorAsync(workspaceId, http, db, tenant, clock, ct);
        if (opened is null)
        {
            return Results.Forbid();
        }

        return Result(await backup.CreateDestinationAsync(opened.Value.Workspace, opened.Value.Actor, Key(http), request.Name, request.Kind, request.Host, request.Port, request.RemotePath, request.Username, request.Bucket, request.UseSsl, request.Secret, ct));
    }

    private static async Task<IResult> TestAsync(Guid workspaceId, Guid destinationId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, ChatBackupService backup, CancellationToken ct)
    {
        var workspace = await OpenAsync(workspaceId, http, db, tenant, clock, ct);
        return workspace is null ? Results.Forbid() : Result(await backup.TestAsync(workspace, destinationId, ct));
    }

    private static async Task<IResult> RotateAsync(Guid workspaceId, Guid destinationId, ChatBackupRotateRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, ChatBackupService backup, CancellationToken ct)
    {
        var opened = await OpenActorAsync(workspaceId, http, db, tenant, clock, ct);
        if (opened is null)
        {
            return Results.Forbid();
        }

        return Result(await backup.RotateAsync(opened.Value.Workspace, opened.Value.Actor, destinationId, Key(http), request.Secret, ct));
    }

    private static async Task<IResult> DeleteAsync(Guid workspaceId, Guid destinationId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, ChatBackupService backup, CancellationToken ct)
    {
        var opened = await OpenActorAsync(workspaceId, http, db, tenant, clock, ct);
        if (opened is null)
        {
            return Results.Forbid();
        }

        return Result(await backup.DeleteDestinationAsync(opened.Value.Workspace, opened.Value.Actor, destinationId, ct));
    }

    private static async Task<IResult> CreateJobAsync(Guid workspaceId, ChatBackupJobRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, ChatBackupService backup, CancellationToken ct)
    {
        var opened = await OpenActorAsync(workspaceId, http, db, tenant, clock, ct);
        if (opened is null)
        {
            return Results.Forbid();
        }

        return Result(await backup.CreateJobAsync(opened.Value.Workspace, opened.Value.Actor, Key(http), request.Name, request.DestinationId, request.IncludeAttachments, request.IntervalMinutes, request.Cron, request.RetentionCount, request.RetentionDays, ct));
    }

    private static async Task<IResult> UpdateJobAsync(Guid workspaceId, Guid jobId, ChatBackupJobPatch request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, ChatBackupService backup, CancellationToken ct)
    {
        var opened = await OpenActorAsync(workspaceId, http, db, tenant, clock, ct);
        if (opened is null)
        {
            return Results.Forbid();
        }

        return Result(await backup.UpdateJobAsync(opened.Value.Workspace, opened.Value.Actor, jobId, request.Paused, request.Name, request.DestinationId, request.IncludeAttachments, request.IntervalMinutes, request.Cron, request.RetentionCount, request.RetentionDays, ct));
    }

    private static async Task<IResult> RunAsync(Guid workspaceId, Guid jobId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, ChatBackupService backup, CancellationToken ct)
    {
        var opened = await OpenActorAsync(workspaceId, http, db, tenant, clock, ct);
        if (opened is null)
        {
            return Results.Forbid();
        }

        return Result(await backup.RunAsync(opened.Value.Workspace, opened.Value.Actor, jobId, Key(http), ct));
    }

    private static async Task<IResult> ExportAsync(Guid workspaceId, bool includeAttachments, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, ChatBackupService backup, CancellationToken ct)
    {
        var opened = await OpenActorAsync(workspaceId, http, db, tenant, clock, ct);
        if (opened is null)
        {
            return Results.Forbid();
        }

        return Result(await backup.ExportAsync(opened.Value.Workspace, opened.Value.Actor, includeAttachments, ct));
    }

    private static async Task<IResult> ValidateAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, ChatBackupService backup, CancellationToken ct)
    {
        var opened = await OpenActorAsync(workspaceId, http, db, tenant, clock, ct);
        if (opened is null)
        {
            return Results.Forbid();
        }

        var package = await ReadPackageAsync(http, ct);
        return package.Error is not null
            ? Results.Json(new { error = package.Error }, statusCode: ChatBackupErrors.Status(package.Error))
            : Result(await backup.ValidateImportAsync(opened.Value.Workspace, opened.Value.Actor, package.Bytes!, ct));
    }

    private static async Task<IResult> ExecuteAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, ChatBackupService backup, CancellationToken ct)
    {
        var opened = await OpenActorAsync(workspaceId, http, db, tenant, clock, ct);
        if (opened is null)
        {
            return Results.Forbid();
        }

        var confirm = http.Request.Query["confirm"] == "true";
        var package = await ReadPackageAsync(http, ct);
        return package.Error is not null
            ? Results.Json(new { error = package.Error }, statusCode: ChatBackupErrors.Status(package.Error))
            : Result(await backup.ExecuteImportAsync(opened.Value.Workspace, opened.Value.Actor, Key(http), confirm, package.Bytes!, ct));
    }

    private static async Task<Workspace?> OpenAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        return await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    }

    private static async Task<(UserId Actor, Workspace Workspace)?> OpenActorAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        return workspace is null ? null : (profile.Id, workspace);
    }

    private static async Task<(byte[]? Bytes, string? Error)> ReadPackageAsync(HttpContext http, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var rented = new byte[81920];
        long total = 0;
        int read;
        while ((read = await http.Request.Body.ReadAsync(rented, ct)) > 0)
        {
            total += read;
            if (total > ChatBackupLimits.MaxPackageBytes)
            {
                return (null, ChatBackupErrors.TooLarge);
            }

            buffer.Write(rented, 0, read);
        }

        return buffer.Length == 0 ? (null, ChatBackupErrors.PackageInvalid) : (buffer.ToArray(), null);
    }

    private static string? Key(HttpContext http) =>
        http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) && keys.Count == 1 ? keys[0] : null;

    private static IResult Result(ChatBackupOutcome outcome) =>
        outcome.File is not null
            ? Results.File(outcome.File, "application/zip", "vibechat-chat-backup.zip")
            : outcome.Error is null
                ? Results.Json(outcome.Body, statusCode: outcome.Status)
                : Results.Json(new { error = outcome.Error }, statusCode: outcome.Status);
}

internal sealed record ChatBackupDestinationRequest(
    string? Name,
    string? Kind,
    string? Host,
    int Port,
    string? RemotePath,
    string? Username,
    string? Bucket,
    bool UseSsl,
    string? Secret);

internal sealed record ChatBackupRotateRequest(string? Secret);

internal sealed record ChatBackupJobRequest(
    string? Name,
    Guid DestinationId,
    bool IncludeAttachments,
    int IntervalMinutes,
    string? Cron,
    int RetentionCount,
    int RetentionDays);

internal sealed record ChatBackupJobPatch(
    bool? Paused,
    string? Name,
    Guid? DestinationId,
    bool? IncludeAttachments,
    int? IntervalMinutes,
    string? Cron,
    int? RetentionCount,
    int? RetentionDays);
