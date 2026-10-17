using Microsoft.Extensions.Options;
using VibeChat.Api.Contracts;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class ConnectorEndpoints
{
    internal static void MapConnectors(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/connectors/catalog", CatalogAsync)
            .RequirePermission(Permissions.Connector.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/connectors", CreateAsync)
            .RequirePermission(Permissions.Connector.Manage);
        v1.MapGet("/workspaces/{workspaceId:guid}/connectors", ListAsync)
            .RequirePermission(Permissions.Connector.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/rotate", RotateAsync)
            .RequirePermission(Permissions.Connector.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/revoke", RevokeAsync)
            .RequirePermission(Permissions.Connector.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/reconnect", ReconnectAsync)
            .RequirePermission(Permissions.Connector.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/oauth/start", StartOauthAsync)
            .RequirePermission(Permissions.Connector.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/oauth/callback", CallbackAsync)
            .RequirePermission(Permissions.Connector.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/deliveries", DeliverAsync)
            .RequirePermission(Permissions.Connector.Manage);
        v1.MapPost("/connectors/inbound/{opaqueKey}", InboundAsync)
            .AllowAnonymous()
            .AllowPermissionGateExempt("connector signature (B-127)");
    }

    private static IResult CatalogAsync(IOptionsMonitor<ConnectorOptions> options)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var catalog = ConnectorPolicies.Catalog.Select(entry =>
            new ConnectorCatalogResponse(entry.Id, entry.Scopes, entry.Oauth)).ToArray();
        return Results.Ok(catalog);
    }

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        CreateConnectorRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        ConnectorService connectors,
        IOptionsMonitor<ConnectorOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Profile is null || gate.Workspace is null)
        {
            return gate.Error!;
        }

        var result = await connectors.CreateAsync(
            gate.Workspace.TenantId,
            gate.Workspace.Id,
            gate.Profile.Id,
            request.IdempotencyKey,
            request.Provider,
            request.DestinationKind,
            request.DestinationId,
            request.Scopes ?? [],
            request.Token,
            request.ClientId,
            request.BaseUrl,
            ct);
        return ToResult(result, MapInstallation);
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        ConnectorService connectors,
        IOptionsMonitor<ConnectorOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Workspace is null)
        {
            return gate.Error!;
        }

        var rows = await connectors.ListAsync(gate.Workspace.Id, ct);
        return Results.Ok(rows.Select(MapInstallation).ToArray());
    }

    private static async Task<IResult> RotateAsync(
        Guid workspaceId,
        Guid installationId,
        RotateConnectorRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        ConnectorService connectors,
        IOptionsMonitor<ConnectorOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Profile is null || gate.Workspace is null)
        {
            return gate.Error!;
        }

        var result = await connectors.RotateAsync(gate.Workspace.Id, installationId, gate.Profile.Id, request.Token, ct);
        return ToResult(result, MapInstallation);
    }

    private static async Task<IResult> RevokeAsync(
        Guid workspaceId,
        Guid installationId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        ConnectorService connectors,
        IOptionsMonitor<ConnectorOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Profile is null || gate.Workspace is null)
        {
            return gate.Error!;
        }

        var result = await connectors.RevokeAsync(gate.Workspace.Id, installationId, gate.Profile.Id, ct);
        return ToResult(result, MapInstallation);
    }

    private static Task<IResult> ReconnectAsync(
        Guid workspaceId,
        Guid installationId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        ConnectorService connectors,
        IOptionsMonitor<ConnectorOptions> options,
        CancellationToken ct) =>
        DeliveryAsync(workspaceId, installationId, http, db, tenantContext, clock, connectors, options, reconnect: true, ct);

    private static Task<IResult> DeliverAsync(
        Guid workspaceId,
        Guid installationId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        ConnectorService connectors,
        IOptionsMonitor<ConnectorOptions> options,
        CancellationToken ct) =>
        DeliveryAsync(workspaceId, installationId, http, db, tenantContext, clock, connectors, options, reconnect: false, ct);

    private static async Task<IResult> StartOauthAsync(
        Guid workspaceId,
        Guid installationId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        ConnectorService connectors,
        IOptionsMonitor<ConnectorOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Workspace is null)
        {
            return gate.Error!;
        }

        var result = await connectors.StartOauthAsync(gate.Workspace.Id, installationId, clientId: null, ct);
        return ToResult(result, start => new { authorizeUrl = start.AuthorizeUrl, scopes = start.Scopes });
    }

    private static async Task<IResult> CallbackAsync(
        Guid workspaceId,
        Guid installationId,
        ConnectorOauthCallbackRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        ConnectorService connectors,
        IOptionsMonitor<ConnectorOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Workspace is null)
        {
            return gate.Error!;
        }

        var result = await connectors.CompleteOauthAsync(
            gate.Workspace.Id, installationId, request.Code, request.State, ct);
        return ToResult(result, MapInstallation);
    }

    private static async Task<IResult> InboundAsync(
        string opaqueKey,
        HttpContext http,
        ConnectorService connectors,
        IOptionsMonitor<ConnectorOptions> options,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var body = await ReadBodyAsync(http, ct);
        if (body.Length > ConnectorPolicies.MaxBodyChars)
        {
            return Results.BadRequest(new { error = "InvalidConnectorEvent" });
        }

        var result = await connectors.AcceptInboundAsync(
            opaqueKey,
            body,
            Header(http, ConnectorPolicies.SignatureHeader),
            Header(http, ConnectorPolicies.GitHubSignatureHeader),
            Header(http, ConnectorPolicies.GitLabTokenHeader),
            ct);
        return ToResult(result, state => new { effectId = state.EffectId, duplicate = state.Duplicate });
    }

    private static async Task<IResult> DeliveryAsync(
        Guid workspaceId,
        Guid installationId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        ConnectorService connectors,
        IOptionsMonitor<ConnectorOptions> options,
        bool reconnect,
        CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenAsync(workspaceId, http, db, tenantContext, clock, ct);
        if (gate.Error is not null || gate.Workspace is null)
        {
            return gate.Error!;
        }

        var result = reconnect
            ? await connectors.ReconnectAsync(gate.Workspace.Id, installationId, ct)
            : await connectors.DeliverAsync(gate.Workspace.Id, installationId, ct);
        return ToResult(result, state => new { ok = state.Ok, error = state.ErrorCode });
    }

    private static async Task<WorkspaceGate> OpenAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenantContext, ct);
        return workspace is null
            ? new WorkspaceGate(null, profile, Results.Forbid())
            : new WorkspaceGate(workspace, profile, null);
    }

    private static async Task<string> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        using var reader = new StreamReader(http.Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        return body.Length > ConnectorPolicies.MaxBodyChars + 1
            ? body[..(ConnectorPolicies.MaxBodyChars + 1)]
            : body;
    }

    private static string? Header(HttpContext http, string name) =>
        http.Request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;

    private static IResult ToResult<T>(ConnectorCall<T> result, Func<T, object> map)
    {
        if (result.Error is not null || result.Value is null)
        {
            return result.Status switch
            {
                StatusCodes.Status401Unauthorized => Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status401Unauthorized),
                StatusCodes.Status404NotFound => Results.NotFound(new { error = result.Error }),
                StatusCodes.Status409Conflict => Results.Conflict(new { error = result.Error }),
                StatusCodes.Status410Gone => Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status410Gone),
                _ => Results.BadRequest(new { error = result.Error }),
            };
        }

        return Results.Ok(map(result.Value));
    }

    private static ConnectorInstallationResponse MapInstallation(ConnectorInstallationState state) =>
        new(
            state.Id,
            state.Provider,
            state.Status,
            state.DestinationKind,
            state.DestinationId,
            state.Scopes,
            state.SecretMask,
            state.Circuit,
            state.LastErrorCode,
            state.SecretVersion,
            state.InboundPath);

    private static IResult Disabled() => Results.NotFound(new { error = "ConnectorDisabled" });

    private sealed record WorkspaceGate(Workspace? Workspace, UserProfile? Profile, IResult? Error);
}
