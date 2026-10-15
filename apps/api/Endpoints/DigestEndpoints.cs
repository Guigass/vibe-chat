using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Notifications;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class DigestEndpoints
{
    internal static void MapDigests(this RouteGroupBuilder v1)
    {
        v1.MapGet("/notifications/digests/settings", GetSettingsAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPut("/notifications/digests/settings", (Delegate)PutSettingsAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/digests/catch-up", (Delegate)CatchUpAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/digests/latest", LatestAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/digests/{digestId:guid}", ReadAsync)
            .RequirePermission(Permissions.Message.Read);
    }

    private static async Task<IResult> GetSettingsAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        DigestGenerator digests,
        IOptionsMonitor<DigestOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Results.NotFound(new { error = "DigestDisabled" });
        }

        var gate = await OpenUserAsync(http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var schedule = await digests.GetScheduleAsync(gate.TenantId, gate.UserId, ct);
        return Results.Ok(ToSettings(schedule));
    }

    private static async Task<IResult> PutSettingsAsync(
        DigestSettingsRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptionsMonitor<DigestOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Results.NotFound(new { error = "DigestDisabled" });
        }

        if (!DigestPolicies.TryParseCadence(request.Cadence, out var cadence)
            || !DigestPolicies.TryParseDelivery(request.Delivery, out var delivery)
            || !DigestPolicies.TryTimeZone(request.TimeZone, out _)
            || request.Weekday is < 0 or > 6
            || !TimeOnly.TryParse(request.LocalTime, out var localTime))
        {
            return Results.BadRequest(new { error = "InvalidDigestSettings" });
        }

        var channels = (request.ChannelIds ?? []).Where(id => id != Guid.Empty).Distinct().Take(DigestPolicies.MaxChannels + 1).ToArray();
        if (channels.Length > DigestPolicies.MaxChannels)
        {
            return Results.BadRequest(new { error = "InvalidDigestSettings" });
        }

        var gate = await OpenUserAsync(http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var now = clock.UtcNow;
        var preference = await db.NotificationPreferences.FirstOrDefaultAsync(row => row.UserId == gate.UserId, ct);
        if (preference is null)
        {
            preference = new NotificationPreference
            {
                Id = Guid.NewGuid(),
                TenantId = gate.TenantId,
                UserId = gate.UserId,
            };
            db.NotificationPreferences.Add(preference);
        }

        preference.DigestEnabled = request.Enabled;
        preference.TimeZone = request.TimeZone.Trim();
        var schedule = await db.DigestSchedules.FirstOrDefaultAsync(row => row.UserId == gate.UserId, ct);
        if (schedule is null)
        {
            schedule = DigestScheduleSync.CreateDefault(preference, now);
            db.DigestSchedules.Add(schedule);
        }

        schedule.Enabled = request.Enabled;
        schedule.Cadence = cadence;
        schedule.LocalTime = localTime;
        schedule.Weekday = request.Weekday;
        schedule.TimeZone = request.TimeZone.Trim();
        schedule.Delivery = delivery;
        schedule.IncludeAi = request.IncludeAi;
        schedule.ChannelIds = channels;
        schedule.UpdatedAt = now;
        schedule.NextCheckAt = null;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToSettings(schedule));
    }

    private static async Task<IResult> CatchUpAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        DigestGenerator digests,
        IOptionsMonitor<DigestOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Results.NotFound(new { error = "DigestDisabled" });
        }

        if (!DigestPolicies.TryNormalizeIdempotency(http.Request.Headers["Idempotency-Key"].ToString(), out var idempotency))
        {
            return Results.BadRequest(new { error = "InvalidIdempotencyKey" });
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var schedule = await digests.GetScheduleAsync(gate.TenantId, gate.UserId, ct);
        var periodKey = DigestPolicies.PeriodKey(DigestCadence.OnDemand, clock.UtcNow.UtcDateTime, idempotency);
        var run = await digests.GenerateAsync(
            gate.TenantId,
            gate.WorkspaceId,
            gate.UserId,
            gate.IsWorkspaceMember,
            DigestCadence.OnDemand,
            periodKey,
            schedule.Delivery,
            schedule.IncludeAi,
            schedule.ChannelIds ?? [],
            emitNew: true,
            ct);
        var items = await digests.VisibleItemsAsync(run, gate.UserId, gate.IsWorkspaceMember, ct);
        return Results.Ok(ToDigest(run, items));
    }

    private static Task<IResult> LatestAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        DigestGenerator digests,
        IOptionsMonitor<DigestOptions> options,
        CancellationToken ct) =>
        ReadCoreAsync(workspaceId, digestId: null, latest: true, http, db, tenant, clock, digests, options, ct);

    private static Task<IResult> ReadAsync(
        Guid workspaceId,
        Guid digestId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        DigestGenerator digests,
        IOptionsMonitor<DigestOptions> options,
        CancellationToken ct) =>
        ReadCoreAsync(workspaceId, digestId, latest: false, http, db, tenant, clock, digests, options, ct);

    private static async Task<IResult> ReadCoreAsync(
        Guid workspaceId,
        Guid? digestId,
        bool latest,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        DigestGenerator digests,
        IOptionsMonitor<DigestOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Results.NotFound(new { error = "DigestDisabled" });
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var query = db.DigestRuns.AsNoTracking().Where(row => row.UserId == gate.UserId && row.WorkspaceId == gate.WorkspaceId);
        var run = latest
            ? await query.OrderByDescending(row => row.CreatedAt).FirstOrDefaultAsync(ct)
            : await query.FirstOrDefaultAsync(row => row.Id == digestId, ct);
        if (run is null)
        {
            return Results.NotFound(new { error = "DigestNotFound" });
        }

        var items = await digests.VisibleItemsAsync(run, gate.UserId, gate.IsWorkspaceMember, ct);
        return Results.Ok(ToDigest(run, items));
    }

    private static async Task<UserGate> OpenUserAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        await RequestAuth.BeginRlsUserAsync(db, tenant, profile.Id, ct);
        if (!tenant.HasTenant)
        {
            return new UserGate(default, default, Results.Forbid());
        }

        return new UserGate(tenant.TenantId, profile.Id, null);
    }

    private static async Task<WorkspaceGate> OpenWorkspaceAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var (workspace, isGuest) = await RequestAuth.ResolveWorkspaceOrGuestAsync(
            new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return new WorkspaceGate(default, default, default, false, Results.Forbid());
        }

        return new WorkspaceGate(workspace.TenantId, workspace.Id, profile.Id, !isGuest, null);
    }

    private static DigestSettingsResponse ToSettings(DigestSchedule schedule) => new(
        schedule.Enabled,
        schedule.Cadence,
        schedule.LocalTime.ToString("HH:mm"),
        schedule.Weekday,
        schedule.TimeZone,
        schedule.Delivery,
        schedule.IncludeAi,
        schedule.ChannelIds ?? []);

    private static DigestResponse ToDigest(DigestRun run, IReadOnlyList<DigestVisibleItem> items)
    {
        var sections = DigestItemKind.All.Select(kind => new DigestSectionResponse(
            kind,
            items.Where(item => item.Kind == kind)
                .Select(item => new DigestItemResponse(item.SourceId, item.ChannelId, item.ChannelName, item.MessageId, item.Sequence))
                .ToArray())).ToArray();
        return new DigestResponse(
            run.Id,
            run.Status,
            run.AiStatus,
            run.Summary,
            run.Delivery,
            run.DeliveryCount,
            run.DeliveredAt is null,
            sections);
    }

    private readonly record struct UserGate(TenantId TenantId, UserId UserId, IResult? Error);

    private readonly record struct WorkspaceGate(
        TenantId TenantId,
        WorkspaceId WorkspaceId,
        UserId UserId,
        bool IsWorkspaceMember,
        IResult? Error);
}

internal sealed record DigestSettingsRequest(
    bool Enabled,
    string? Cadence,
    string? LocalTime,
    short Weekday,
    string? TimeZone,
    string? Delivery,
    bool IncludeAi,
    Guid[]? ChannelIds);

internal sealed record DigestSettingsResponse(
    bool Enabled,
    string Cadence,
    string LocalTime,
    short Weekday,
    string TimeZone,
    string Delivery,
    bool IncludeAi,
    Guid[] ChannelIds);

internal sealed record DigestResponse(
    Guid Id,
    string Status,
    string AiStatus,
    string? Summary,
    string Delivery,
    int DeliveryCount,
    bool ExternalDeferred,
    DigestSectionResponse[] Sections);

internal sealed record DigestSectionResponse(string Kind, DigestItemResponse[] Items);

internal sealed record DigestItemResponse(Guid SourceId, Guid ChannelId, string ChannelName, Guid? MessageId, long? Sequence);
