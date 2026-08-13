using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api;

/// <summary>
/// B-174: reads <see cref="RequirePermissionAttribute"/> metadata and enforces
/// <see cref="IPermissionChecker"/> after resolving tenant from route membership.
/// </summary>
internal sealed class RequirePermissionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var endpoint = http.GetEndpoint();
        var required = endpoint?.Metadata.GetOrderedMetadata<RequirePermissionAttribute>();
        if (required is null || required.Count == 0)
        {
            return await next(context);
        }

        var permissions = required.Select(x => x.Permission).Distinct(StringComparer.Ordinal).ToArray();
        var db = http.RequestServices.GetRequiredService<VibeChatDbContext>();
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        var checker = http.RequestServices.GetRequiredService<IPermissionChecker>();
        var clock = http.RequestServices.GetRequiredService<IClock>();

        UserId userId;
        try
        {
            var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, http.RequestAborted);
            userId = profile.Id;
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }

        var route = http.Request.RouteValues;
        TenantId? tenantId = null;
        var path = http.Request.Path.Value ?? string.Empty;
        // B-175: /admin/* must not require channel/thread membership (conversation audit
        // bypasses channel_members). Resolve tenant via admin membership + permission.
        var isAdminSurface = path.Contains("/admin/", StringComparison.OrdinalIgnoreCase);

        if (isAdminSurface)
        {
            Guid? preferred = null;
            if (TryGetGuid(route, "workspaceId", out var adminWorkspaceId))
            {
                preferred = adminWorkspaceId;
            }
            else if (http.Request.Query.TryGetValue("workspaceId", out var q)
                && Guid.TryParse(q, out var qid))
            {
                preferred = qid;
            }

            tenantId = await RequestAuth.ResolveAdminTenantAsync(
                userId, db, tenant, checker, permissions, preferred, http.RequestAborted);
            if (tenantId is null)
            {
                return Results.Forbid();
            }
        }
        else if (TryGetGuid(route, "channelId", out var channelId))
        {
            var channel = await RequestAuth.ResolveChannelAsync(
                new ChannelId(channelId), userId, db, tenant, http.RequestAborted);
            if (channel is null)
            {
                return Results.Forbid();
            }

            tenantId = channel.TenantId;
        }
        else if (TryGetGuid(route, "threadId", out var threadId))
        {
            await RequestAuth.BeginRlsUserAsync(db, tenant, userId, http.RequestAborted);
            var thread = await db.MessageThreads.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == threadId, http.RequestAborted);
            if (thread is null)
            {
                return Results.NotFound();
            }

            var channel = await RequestAuth.ResolveChannelAsync(
                thread.ChannelId, userId, db, tenant, http.RequestAborted);
            if (channel is null)
            {
                return Results.Forbid();
            }

            tenantId = channel.TenantId;
        }
        else if (TryGetGuid(route, "workspaceId", out var workspaceId))
        {
            var workspace = await RequestAuth.ResolveWorkspaceAsync(
                new WorkspaceId(workspaceId), userId, db, tenant, http.RequestAborted);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            tenantId = workspace.TenantId;
        }
        else
        {
            Guid? preferred = null;
            if (http.Request.Query.TryGetValue("workspaceId", out var q) && Guid.TryParse(q, out var qid))
            {
                preferred = qid;
            }

            tenantId = await RequestAuth.ResolveAdminTenantAsync(
                userId, db, tenant, checker, permissions, preferred, http.RequestAborted);
            if (tenantId is null)
            {
                return Results.Forbid();
            }
        }

        foreach (var permission in permissions)
        {
            if (!await checker.HasPermissionAsync(tenantId.Value, userId, permission, http.RequestAborted))
            {
                return Results.Forbid();
            }
        }

        return await next(context);
    }

    private static bool TryGetGuid(RouteValueDictionary route, string key, out Guid value)
    {
        value = default;
        if (!route.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        return Guid.TryParse(raw.ToString(), out value);
    }
}

internal static class RequirePermissionEndpointExtensions
{
    /// <summary>
    /// Declares required permission(s) for the endpoint. The group-level
    /// <see cref="RequirePermissionFilter"/> enforces them (B-174).
    /// </summary>
    public static RouteHandlerBuilder RequirePermission(
        this RouteHandlerBuilder builder,
        params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (permissions is null || permissions.Length == 0)
        {
            throw new ArgumentException("At least one permission is required.", nameof(permissions));
        }

        foreach (var permission in permissions)
        {
            builder.WithMetadata(new RequirePermissionAttribute(permission));
        }

        return builder;
    }

    /// <summary>
    /// Marks a mutable endpoint that intentionally keeps handler-local authZ
    /// (conditional own/any, membership-only, or lab-only). Required by the B-174 CI gate.
    /// </summary>
    public static RouteHandlerBuilder AllowPermissionGateExempt(
        this RouteHandlerBuilder builder,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new PermissionGateExemptAttribute(reason));
    }
}

/// <summary>B-174 CI allowlist marker for mutable routes without <see cref="RequirePermissionAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
internal sealed class PermissionGateExemptAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
