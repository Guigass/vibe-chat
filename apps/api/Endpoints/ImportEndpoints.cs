using System.Text.Json;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Api;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class ImportEndpoints
{
    internal static void MapImports(this RouteGroupBuilder v1)
    {
        v1.MapPost("/workspaces/{workspaceId:guid}/imports", CreateAsync).RequirePermission(Permissions.Workspace.Import);
        v1.MapGet("/workspaces/{workspaceId:guid}/imports", ListAsync).RequirePermission(Permissions.Workspace.Import);
        v1.MapGet("/workspaces/{workspaceId:guid}/imports/{importId:guid}", GetAsync).RequirePermission(Permissions.Workspace.Import);
        v1.MapGet("/workspaces/{workspaceId:guid}/imports/{importId:guid}/report", ReportAsync).RequirePermission(Permissions.Workspace.Import);
        v1.MapPost("/workspaces/{workspaceId:guid}/imports/{importId:guid}/plan", PlanAsync).RequirePermission(Permissions.Workspace.Import);
        v1.MapPost("/workspaces/{workspaceId:guid}/imports/{importId:guid}/execute", ExecuteAsync).RequirePermission(Permissions.Workspace.Import);
        v1.MapPost("/workspaces/{workspaceId:guid}/imports/{importId:guid}/pause", PauseAsync).RequirePermission(Permissions.Workspace.Import);
        v1.MapPost("/workspaces/{workspaceId:guid}/imports/{importId:guid}/resume", ResumeAsync).RequirePermission(Permissions.Workspace.Import);
        v1.MapPost("/workspaces/{workspaceId:guid}/imports/{importId:guid}/publish", PublishAsync).RequirePermission(Permissions.Workspace.Import);
        v1.MapPost("/workspaces/{workspaceId:guid}/imports/{importId:guid}/rollback", RollbackAsync).RequirePermission(Permissions.Workspace.Import);
    }

    private static Task<IResult> PlanAsync(Guid workspaceId, Guid importId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<ImportOptions> options, WorkspaceImportService imports, CancellationToken ct) =>
        CommandAsync(workspaceId, http, db, tenant, permissions, clock, options, imports, (service, workspace, profile, token) => service.PlanAsync(workspace, profile.Id, importId, token), ct);

    private static Task<IResult> ExecuteAsync(Guid workspaceId, Guid importId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<ImportOptions> options, WorkspaceImportService imports, CancellationToken ct) =>
        CommandAsync(workspaceId, http, db, tenant, permissions, clock, options, imports, (service, workspace, profile, token) => service.ExecuteAsync(workspace, profile.Id, importId, token), ct);

    private static Task<IResult> PauseAsync(Guid workspaceId, Guid importId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<ImportOptions> options, WorkspaceImportService imports, CancellationToken ct) =>
        CommandAsync(workspaceId, http, db, tenant, permissions, clock, options, imports, (service, workspace, _, token) => service.PauseAsync(workspace, importId, token), ct);

    private static Task<IResult> ResumeAsync(Guid workspaceId, Guid importId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<ImportOptions> options, WorkspaceImportService imports, CancellationToken ct) =>
        CommandAsync(workspaceId, http, db, tenant, permissions, clock, options, imports, (service, workspace, _, token) => service.ResumeAsync(workspace, importId, token), ct);

    private static Task<IResult> PublishAsync(Guid workspaceId, Guid importId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<ImportOptions> options, WorkspaceImportService imports, CancellationToken ct) =>
        CommandAsync(workspaceId, http, db, tenant, permissions, clock, options, imports, (service, workspace, profile, token) => service.PublishAsync(workspace, profile.Id, importId, token), ct);

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<ImportOptions> options,
        WorkspaceImportService imports,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]) || keys[0]!.Length > 200)
        {
            return Problem(400, ImportErrors.IdempotencyRequired);
        }

        var body = await http.Request.ReadFromJsonAsync<CreateImportBody>(ct);
        if (body is null || body.Document.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Problem(400, ImportErrors.DocumentInvalid);
        }

        var raw = body.Document.GetRawText();
        if (raw.Length > ImportLimits.MaxDocumentBytes)
        {
            return Problem(413, ImportErrors.Quota);
        }

        var result = await imports.ValidateAsync(access.Workspace!, access.Profile!.Id, body.Adapter, raw, keys[0]!, ct);
        return ToResult(result);
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<ImportOptions> options,
        WorkspaceImportService imports,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var jobs = await imports.ListAsync(access.Workspace!, ct);
        return Results.Ok(jobs.Select(job => new { id = job.Id, status = job.Status, adapter = job.Adapter, createdAt = job.CreatedAt }));
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        Guid importId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<ImportOptions> options,
        WorkspaceImportService imports,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var job = await imports.FindAsync(access.Workspace!, importId, ct);
        return job is null ? Problem(404, ImportErrors.NotFound) : Results.Ok(View(job, false));
    }

    private static async Task<IResult> ReportAsync(
        Guid workspaceId,
        Guid importId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<ImportOptions> options,
        WorkspaceImportService imports,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var job = await imports.FindAsync(access.Workspace!, importId, ct);
        return job is null ? Problem(404, ImportErrors.NotFound) : Results.Ok(WorkspaceImportService.DeserializeReport(job));
    }

    private static async Task<IResult> RollbackAsync(
        Guid workspaceId,
        Guid importId,
        RollbackImportBody? body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<ImportOptions> options,
        WorkspaceImportService imports,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var result = await imports.RollbackAsync(access.Workspace!, access.Profile!.Id, importId, body?.Confirm == true, ct);
        return ToResult(result);
    }

    private static async Task<IResult> CommandAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<ImportOptions> options,
        WorkspaceImportService imports,
        Func<WorkspaceImportService, VibeChat.Tenancy.Workspace, VibeChat.Identity.UserProfile, CancellationToken, Task<ImportCommandResult>> command,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var result = await command(imports, access.Workspace!, access.Profile!, ct);
        return ToResult(result);
    }

    private static async Task<(IResult? Error, VibeChat.Identity.UserProfile? Profile, VibeChat.Tenancy.Workspace? Workspace)> AccessAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<ImportOptions> options,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null || !await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Workspace.Import, ct))
        {
            return (Results.Forbid(), null, null);
        }

        if (!options.Value.Enabled)
        {
            return (Problem(404, ImportErrors.Disabled), null, null);
        }

        return (null, profile, workspace);
    }

    private static IResult ToResult(ImportCommandResult result)
    {
        if (result.Error is not null || result.Job is null)
        {
            return Problem(result.StatusCode, result.Error ?? ImportErrors.DocumentInvalid);
        }

        return Results.Json(View(result.Job, result.Idempotent), statusCode: result.StatusCode);
    }

    private static object View(ImportJobRecord job, bool idempotent) => new
    {
        id = job.Id,
        status = job.Status,
        adapter = job.Adapter,
        createdAt = job.CreatedAt,
        updatedAt = job.UpdatedAt,
        idempotent,
        report = WorkspaceImportService.DeserializeReport(job)
    };

    private static IResult Problem(int status, string error) =>
        Results.Json(new { error }, statusCode: status);

    private sealed class CreateImportBody
    {
        public string Adapter { get; set; } = string.Empty;
        public JsonElement Document { get; set; }
    }

    private sealed class RollbackImportBody
    {
        public bool Confirm { get; set; }
    }
}
