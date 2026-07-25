using System.IO.Compression;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.Realtime;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:4200", "https://localhost:4200", "https://localhost:8443"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("localhost", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // Reference proxy may sit on Docker bridge / host network; clear known nets for self-host.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var auth = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "smart";
    options.DefaultChallengeScheme = "smart";
});

auth.AddPolicyScheme("smart", "JWT or DevAuth", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        if (!builder.Environment.IsDevelopment())
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        var hasDevHeader = context.Request.Headers.ContainsKey("X-Dev-User");
        var hasDevQuery = context.Request.Query.ContainsKey("devUser");
        return hasDevHeader || hasDevQuery
            ? DevAuthHandler.SchemeName
            : JwtBearerDefaults.AuthenticationScheme;
    };
});

auth.AddJwtBearer(options =>
{
    options.Authority = builder.Configuration["Authentication:Authority"];
    options.Audience = builder.Configuration["Authentication:Audience"];
    options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", true);
    // Compose apps (B-074): Authority = public issuer (browser iss); MetadataAddress = in-network discovery.
    var metadataAddress = builder.Configuration["Authentication:MetadataAddress"];
    if (!string.IsNullOrWhiteSpace(metadataAddress))
    {
        options.MetadataAddress = metadataAddress;
    }

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});
auth.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });

builder.Services.AddAuthorization();
builder.Services.AddScoped<ICurrentUser>(sp =>
{
    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
    return new CurrentUser(accessor.HttpContext?.User ?? new ClaimsPrincipal());
});

builder.Services.AddVibeChatInfrastructure(builder.Configuration);

var signalR = builder.Services.AddSignalR().AddMessagePackProtocol();
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    signalR.AddStackExchangeRedis(redisConnection);
}

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("VibeChat.Api"))
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddMeter(VibeChatMetrics.MeterName));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    if (app.Configuration.GetValue("Seed:Enabled", false))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SeedData>().SeedAsync(CancellationToken.None);
    }
}

app.UseExceptionHandler();
app.UseForwardedHeaders();
app.UseCors("localhost");
app.UseAuthentication();
app.UseAuthorization();

var v1 = app.MapGroup("/api/v1").RequireAuthorization();

v1.MapGet("/me", async (HttpContext http, VibeChatDbContext db, IClock clock, IAuditWriter audit, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var roles = await db.WorkspaceMembers.IgnoreQueryFilters().Where(x => x.UserId == profile.Id).Select(x => x.Role).Distinct().ToArrayAsync(ct);
    if (roles.Any(x => x is Role.Admin or Role.PlatformOwner or Role.WorkspaceOwner))
    {
        audit.Add(new AuditEvent
        {
            TenantId = SeedData.DemoTenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.AdminLogin,
            EntityType = "UserProfile",
            EntityId = profile.Id.ToString()
        });
        await db.SaveChangesAsync(ct);
    }

    return Results.Ok(new MeResponse(profile.Id.Value, profile.Subject, profile.Email, profile.DisplayName, roles.Select(x => x.ToString()).ToArray()));
});

v1.MapGet("/workspaces", async (HttpContext http, VibeChatDbContext db, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspaces = await db.WorkspaceMembers.IgnoreQueryFilters()
        .Where(x => x.UserId == profile.Id)
        .Join(db.Workspaces.IgnoreQueryFilters(), m => m.WorkspaceId, w => w.Id, (m, w) => new WorkspaceResponse(w.Id.Value, w.Name, w.Slug, m.Role.ToString()))
        .ToArrayAsync(ct);
    return Results.Ok(workspaces);
});

v1.MapGet("/workspaces/{workspaceId:guid}/channels", async (Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null)
    {
        return Results.Forbid();
    }

    var memberChannelIds = await db.ChannelMembers
        .Where(x => x.UserId == profile.Id)
        .Select(x => x.ChannelId)
        .ToListAsync(ct);

    var channels = await db.Channels
        .Where(x => x.WorkspaceId == workspace.Id
            && (x.Type == ChannelType.Public || x.Type == ChannelType.Announcement || memberChannelIds.Contains(x.Id)))
        .OrderBy(x => x.Type == ChannelType.Direct ? 1 : 0)
        .ThenBy(x => x.Name)
        .ToListAsync(ct);

    var peerByChannel = await ResolveDirectPeersAsync(channels, profile.Id, db, ct);
    var response = channels.Select(x =>
    {
        peerByChannel.TryGetValue(x.Id, out var peer);
        var displayName = x.Type == ChannelType.Direct && peer is not null ? peer.DisplayName : x.Name;
        return new ChannelResponse(
            x.Id.Value,
            x.WorkspaceId.Value,
            displayName,
            x.Type.ToString(),
            peer?.UserId.Value,
            peer?.DisplayName,
            x.SpaceId);
    }).ToArray();
    return Results.Ok(response);
});

v1.MapGet("/workspaces/{workspaceId:guid}/spaces", async (Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null)
    {
        return Results.Forbid();
    }

    var spaces = await db.Spaces
        .Where(x => x.WorkspaceId == workspace.Id)
        .OrderBy(x => x.Order)
        .ThenBy(x => x.Name)
        .Select(x => new SpaceResponse(x.Id, x.WorkspaceId.Value, x.Name, x.Order))
        .ToArrayAsync(ct);

    return Results.Ok(spaces);
});

v1.MapPost("/workspaces/{workspaceId:guid}/spaces", async (Guid workspaceId, CreateSpaceRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IAuditWriter audit, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null || !await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Channel.Create, ct))
    {
        return Results.Forbid();
    }

    var name = (request.Name ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name))
    {
        return Results.BadRequest(new { error = "name is required." });
    }

    var maxOrder = await db.Spaces.Where(x => x.WorkspaceId == workspace.Id).Select(x => (int?)x.Order).MaxAsync(ct) ?? -1;
    var space = new Space
    {
        Id = Guid.NewGuid(),
        TenantId = workspace.TenantId,
        WorkspaceId = workspace.Id,
        Name = name,
        Order = request.Order ?? maxOrder + 1,
        CreatedAt = clock.UtcNow
    };
    db.Spaces.Add(space);
    audit.Add(new AuditEvent
    {
        TenantId = workspace.TenantId,
        ActorUserId = profile.Id,
        Action = AuditActions.SpaceCreate,
        EntityType = "Space",
        EntityId = space.Id.ToString(),
        MetadataJson = JsonSerializer.Serialize(new { workspaceId, name = space.Name })
    });
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/workspaces/{workspaceId}/spaces/{space.Id}", new SpaceResponse(space.Id, space.WorkspaceId.Value, space.Name, space.Order));
});

v1.MapGet("/workspaces/{workspaceId:guid}/members", async (Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null)
    {
        return Results.Forbid();
    }

    var members = await (
        from m in db.WorkspaceMembers
        where m.WorkspaceId == workspace.Id
        join u in db.UserProfiles on m.UserId equals u.Id
        orderby u.DisplayName
        select new WorkspaceMemberResponse(u.Id.Value, u.DisplayName, u.Email, m.Role.ToString())
    ).ToArrayAsync(ct);

    return Results.Ok(members);
});

v1.MapGet("/workspaces/{workspaceId:guid}/roles", async (Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null)
    {
        return Results.Forbid();
    }

    var actorMembership = await db.WorkspaceMembers.AsNoTracking()
        .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == profile.Id, ct);
    if (actorMembership is null || !WorkspaceRolePolicies.CanManageRoles(actorMembership.Role))
    {
        return Results.Forbid();
    }

    return Results.Ok(new WorkspaceRolesResponse(
        WorkspaceRolePolicies.AssignableRoles.Select(r => r.ToString()).ToArray()));
});

// B-068: admin invite / provision membership (no open self-signup).
v1.MapPost("/workspaces/{workspaceId:guid}/members", async (
    Guid workspaceId,
    InviteMemberRequest request,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IOutboxWriter outbox,
    IAuditWriter audit,
    EmailSettingsResolver emailSettings,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null)
    {
        return Results.Forbid();
    }

    var actorMembership = await db.WorkspaceMembers
        .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == profile.Id, ct);
    if (actorMembership is null || !WorkspaceRolePolicies.CanInviteMembers(actorMembership.Role))
    {
        return Results.Forbid();
    }

    var emailAddress = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(emailAddress) || !emailAddress.Contains('@') || emailAddress.Length > 256)
    {
        return Results.BadRequest(new { error = "Valid email is required." });
    }

    var roleValue = string.IsNullOrWhiteSpace(request.Role) ? Role.Member.ToString() : request.Role;
    if (!WorkspaceRolePolicies.TryParseRole(roleValue, out var inviteRole)
        || !WorkspaceRolePolicies.CanAssignInviteRole(actorMembership.Role, inviteRole))
    {
        return Results.BadRequest(new { error = "InvalidRole" });
    }

    var displayName = (request.DisplayName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(displayName))
    {
        displayName = emailAddress.Split('@')[0];
    }

    if (displayName.Length > 160)
    {
        displayName = displayName[..160];
    }

    var pendingSubject = WorkspaceRolePolicies.PendingSubjectForEmail(emailAddress);
    var targetProfile = await db.UserProfiles
        .FirstOrDefaultAsync(x => x.Email.ToLower() == emailAddress || x.Subject == pendingSubject, ct);

    if (targetProfile is null)
    {
        targetProfile = new UserProfile
        {
            Id = UserId.New(),
            Subject = pendingSubject,
            Email = emailAddress,
            DisplayName = displayName,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.UserProfiles.Add(targetProfile);
    }
    else if (string.IsNullOrWhiteSpace(targetProfile.DisplayName)
             || (WorkspaceRolePolicies.IsPendingSubject(targetProfile.Subject)
                 && !string.IsNullOrWhiteSpace(request.DisplayName)))
    {
        targetProfile.DisplayName = displayName;
        targetProfile.UpdatedAt = clock.UtcNow;
    }

    var existing = await db.WorkspaceMembers
        .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == targetProfile.Id, ct);
    if (existing is not null)
    {
        return Results.Conflict(new { error = "AlreadyMember", userId = targetProfile.Id.Value, role = existing.Role.ToString() });
    }

    var membership = new WorkspaceMember
    {
        Id = Guid.NewGuid(),
        TenantId = workspace.TenantId,
        WorkspaceId = workspace.Id,
        UserId = targetProfile.Id,
        Role = inviteRole,
        JoinedAt = clock.UtcNow
    };
    db.WorkspaceMembers.Add(membership);

    audit.Add(new AuditEvent
    {
        TenantId = workspace.TenantId,
        ActorUserId = profile.Id,
        Action = AuditActions.MemberInvite,
        EntityType = "WorkspaceMember",
        EntityId = membership.Id.ToString(),
        MetadataJson = JsonSerializer.Serialize(new
        {
            workspaceId = workspace.Id.Value,
            userId = targetProfile.Id.Value,
            email = emailAddress,
            role = inviteRole.ToString(),
            pending = WorkspaceRolePolicies.IsPendingSubject(targetProfile.Subject)
        })
    });

    if (await emailSettings.IsEnabledAsync(workspace.TenantId, ct)
        && !string.IsNullOrWhiteSpace(targetProfile.Email))
    {
        var subject = $"VibeChat: você foi convidado para {workspace.Name}";
        var body =
            $"Olá {targetProfile.DisplayName},\n\n" +
            $"Você foi adicionado ao workspace \"{workspace.Name}\" com o papel {inviteRole}.\n" +
            "Autentique-se via SSO (Keycloak/OIDC) com este e-mail para acessar.\n" +
            "Não há self-signup aberto — a membership já foi provisionada pelo admin.\n";
        outbox.Add(new OutboxMessage
        {
            TenantId = workspace.TenantId,
            Type = nameof(MemberInvitedEmailEvent),
            Payload = JsonSerializer.Serialize(new MemberInvitedEmailEvent(
                workspace.TenantId.Value,
                workspace.Id.Value,
                targetProfile.Id.Value,
                targetProfile.Email,
                subject,
                body))
        });
    }

    await db.SaveChangesAsync(ct);
    return Results.Created(
        $"/api/v1/workspaces/{workspaceId}/members/{targetProfile.Id.Value}",
        new WorkspaceMemberResponse(targetProfile.Id.Value, targetProfile.DisplayName, targetProfile.Email, membership.Role.ToString()));
});

v1.MapPut("/workspaces/{workspaceId:guid}/members/{userId:guid}/role", async (
    Guid workspaceId,
    Guid userId,
    UpdateMemberRoleRequest request,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IOutboxWriter outbox,
    IAuditWriter audit,
    EmailSettingsResolver emailSettings,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null)
    {
        return Results.Forbid();
    }

    var actorMembership = await db.WorkspaceMembers
        .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == profile.Id, ct);
    if (actorMembership is null || !WorkspaceRolePolicies.CanManageRoles(actorMembership.Role))
    {
        return Results.Forbid();
    }

    if (!WorkspaceRolePolicies.TryParseRole(request.Role, out var newRole))
    {
        return Results.BadRequest(new { error = "InvalidRole" });
    }

    var targetUserId = new UserId(userId);
    var targetMembership = await db.WorkspaceMembers
        .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == targetUserId, ct);
    if (targetMembership is null)
    {
        return Results.NotFound();
    }

    var isSelf = targetMembership.UserId == profile.Id;
    if (!WorkspaceRolePolicies.CanChangeMemberRole(actorMembership.Role, targetMembership.Role, newRole, isSelf))
    {
        return Results.Forbid();
    }

    if (targetMembership.Role == newRole)
    {
        var unchanged = await db.UserProfiles.AsNoTracking().FirstAsync(x => x.Id == targetUserId, ct);
        return Results.Ok(new WorkspaceMemberResponse(unchanged.Id.Value, unchanged.DisplayName, unchanged.Email, targetMembership.Role.ToString()));
    }

    var previousRole = targetMembership.Role;
    targetMembership.Role = newRole;

    var targetProfile = await db.UserProfiles.AsNoTracking().FirstAsync(x => x.Id == targetUserId, ct);
    audit.Add(new AuditEvent
    {
        TenantId = workspace.TenantId,
        ActorUserId = profile.Id,
        Action = AuditActions.MemberRoleChange,
        EntityType = "WorkspaceMember",
        EntityId = targetMembership.Id.ToString(),
        MetadataJson = JsonSerializer.Serialize(new
        {
            workspaceId = workspace.Id.Value,
            userId = targetUserId.Value,
            from = previousRole.ToString(),
            to = newRole.ToString()
        })
    });

    // B-043: optional email via outbox (never on SendMessage hot path). Off by default (D-10).
    if (await emailSettings.IsEnabledAsync(workspace.TenantId, ct)
        && !string.IsNullOrWhiteSpace(targetProfile.Email))
    {
        var subject = $"VibeChat: seu papel em {workspace.Name} foi atualizado";
        var body = $"Olá {targetProfile.DisplayName},\n\nSeu papel no workspace \"{workspace.Name}\" mudou de {previousRole} para {newRole}.\n";
        outbox.Add(new OutboxMessage
        {
            TenantId = workspace.TenantId,
            Type = nameof(MemberRoleChangedEmailEvent),
            Payload = JsonSerializer.Serialize(new MemberRoleChangedEmailEvent(
                workspace.TenantId.Value,
                workspace.Id.Value,
                targetUserId.Value,
                targetProfile.Email,
                subject,
                body))
        });
    }

    await db.SaveChangesAsync(ct);
    return Results.Ok(new WorkspaceMemberResponse(targetProfile.Id.Value, targetProfile.DisplayName, targetProfile.Email, targetMembership.Role.ToString()));
});

v1.MapGet("/workspaces/{workspaceId:guid}/presence", async (Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPresenceService presence, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null)
    {
        return Results.Forbid();
    }

    var memberIds = await db.WorkspaceMembers
        .Where(x => x.WorkspaceId == workspace.Id)
        .Select(x => x.UserId)
        .ToListAsync(ct);
    var statuses = await presence.GetStatusesAsync(workspace.TenantId, memberIds, ct);
    var response = memberIds.Select(id =>
    {
        statuses.TryGetValue(id, out var status);
        return new PresenceResponse(id.Value, status.ToString().ToLowerInvariant());
    }).ToArray();
    return Results.Ok(response);
});

v1.MapPost("/workspaces/{workspaceId:guid}/dms", async (Guid workspaceId, OpenDirectMessageRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null)
    {
        return Results.Forbid();
    }

    if (request.UserId == Guid.Empty || request.UserId == profile.Id.Value)
    {
        return Results.BadRequest(new { error = "userId must reference another workspace member." });
    }

    var peerId = new UserId(request.UserId);
    var peerMember = await db.WorkspaceMembers.FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == peerId, ct);
    var peerProfile = await db.UserProfiles.FirstOrDefaultAsync(x => x.Id == peerId, ct);
    if (peerMember is null || peerProfile is null)
    {
        return Results.NotFound(new { error = "Peer user is not a member of this workspace." });
    }

    var existing = await FindDirectChannelAsync(workspace.Id, profile.Id, peerId, db, ct);
    if (existing is not null)
    {
        return Results.Ok(new ChannelResponse(existing.Id.Value, existing.WorkspaceId.Value, peerProfile.DisplayName, existing.Type.ToString(), peerProfile.Id.Value, peerProfile.DisplayName));
    }

    var channel = new Channel
    {
        Id = ChannelId.New(),
        TenantId = workspace.TenantId,
        WorkspaceId = workspace.Id,
        Name = BuildDirectChannelName(profile.Id, peerId),
        Type = ChannelType.Direct,
        CreatedAt = clock.UtcNow,
        CreatedBy = profile.Id
    };
    db.Channels.Add(channel);
    db.ChannelMembers.AddRange(
        new ChannelMember { Id = Guid.NewGuid(), TenantId = workspace.TenantId, ChannelId = channel.Id, UserId = profile.Id, JoinedAt = clock.UtcNow },
        new ChannelMember { Id = Guid.NewGuid(), TenantId = workspace.TenantId, ChannelId = channel.Id, UserId = peerId, JoinedAt = clock.UtcNow });
    await db.SaveChangesAsync(ct);

    return Results.Created(
        $"/api/v1/channels/{channel.Id.Value}",
        new ChannelResponse(channel.Id.Value, channel.WorkspaceId.Value, peerProfile.DisplayName, channel.Type.ToString(), peerProfile.Id.Value, peerProfile.DisplayName));
});

v1.MapPost("/workspaces/{workspaceId:guid}/channels", async (Guid workspaceId, CreateChannelRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IAuditWriter audit, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null || !await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Channel.Create, ct))
    {
        return Results.Forbid();
    }

    var name = (request.Name ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name))
    {
        return Results.BadRequest(new { error = "name is required." });
    }

    if (Enum.TryParse<ChannelType>(request.Type, true, out var parsed) && parsed is ChannelType.Direct)
    {
        return Results.BadRequest(new { error = "Use POST /workspaces/{id}/dms to open direct messages." });
    }

    Guid? spaceId = null;
    if (request.SpaceId is Guid requestedSpaceId)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(x => x.Id == requestedSpaceId && x.WorkspaceId == workspace.Id, ct);
        if (space is null)
        {
            return Results.BadRequest(new { error = "spaceId must reference a space in this workspace." });
        }

        spaceId = space.Id;
    }

    var channel = new Channel
    {
        Id = ChannelId.New(),
        TenantId = workspace.TenantId,
        WorkspaceId = workspace.Id,
        SpaceId = spaceId,
        Name = name,
        Type = Enum.TryParse<ChannelType>(request.Type, true, out var type) ? type : ChannelType.Public,
        CreatedAt = clock.UtcNow,
        CreatedBy = profile.Id
    };
    db.Channels.Add(channel);
    db.ChannelMembers.Add(new ChannelMember { Id = Guid.NewGuid(), TenantId = workspace.TenantId, ChannelId = channel.Id, UserId = profile.Id, JoinedAt = clock.UtcNow });
    audit.Add(new AuditEvent
    {
        TenantId = workspace.TenantId,
        ActorUserId = profile.Id,
        Action = AuditActions.ChannelCreate,
        EntityType = "Channel",
        EntityId = channel.Id.ToString(),
        MetadataJson = JsonSerializer.Serialize(new { workspaceId, type = channel.Type.ToString(), spaceId })
    });
    await db.SaveChangesAsync(ct);
    return Results.Created(
        $"/api/v1/channels/{channel.Id.Value}",
        new ChannelResponse(channel.Id.Value, channel.WorkspaceId.Value, channel.Name, channel.Type.ToString(), null, null, channel.SpaceId));
});

v1.MapGet("/channels/{channelId:guid}/messages", async (Guid channelId, long? after, int? limit, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var take = Math.Clamp(limit ?? 50, 1, 100);
    var rows = await (
        from m in db.Messages
        where m.ConversationId == channel.Id && m.Sequence > (after ?? 0)
        join u in db.UserProfiles on m.AuthorId equals u.Id into authors
        from u in authors.DefaultIfEmpty()
        orderby m.Sequence
        select new
        {
            Id = m.Id,
            ChannelId = m.ConversationId,
            m.Sequence,
            m.AuthorId,
            m.Body,
            m.CreatedAt,
            m.EditedAt,
            m.DeletedAt,
            m.ThreadId,
            m.ReplyToMessageId,
            AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString()
        })
        .Take(take)
        .ToArrayAsync(ct);

    var messageIds = rows.Select(x => x.Id).ToArray();
    var threadIds = rows.Where(x => x.ThreadId is not null).Select(x => x.ThreadId!.Value).Distinct().ToArray();
    var replyCounts = threadIds.Length == 0
        ? new Dictionary<Guid, int>()
        : await db.Messages.AsNoTracking()
            .Where(m => m.ThreadId != null && threadIds.Contains(m.ThreadId.Value) && m.ConversationId != channel.Id)
            .GroupBy(m => m.ThreadId!.Value)
            .Select(g => new { ThreadId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ThreadId, x => x.Count, ct);

    var attachmentsByMessage = await LoadAttachmentsByMessageAsync(db, channel.Id, messageIds, ct);
    var reactionsByMessage = await LoadReactionSummariesByMessageAsync(db, messageIds, profile.Id, ct);
    var messages = rows.Select(x => new MessageResponse(
        x.Id.Value,
        x.ChannelId.Value,
        x.Sequence,
        x.AuthorId.Value,
        x.DeletedAt == null ? x.Body : string.Empty,
        x.CreatedAt,
        x.EditedAt,
        x.DeletedAt,
        x.AuthorName,
        x.DeletedAt == null && attachmentsByMessage.TryGetValue(x.Id.Value, out var atts) ? atts : [],
        x.ThreadId,
        x.ReplyToMessageId?.Value,
        x.ThreadId is Guid tid && replyCounts.TryGetValue(tid, out var count) ? count : 0,
        x.ChannelId.Value,
        x.DeletedAt == null && reactionsByMessage.TryGetValue(x.Id.Value, out var rx) ? rx : [])).ToArray();
    return Results.Ok(messages);
});

v1.MapPost("/channels/{channelId:guid}/messages", async (
    Guid channelId,
    SendMessageRequest request,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IMessageWriter writer,
    IRateLimiter rateLimiter,
    IConfiguration config,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var sendLimit = config.GetValue("RateLimit:SendPerMinute", RateLimitPolicies.DefaultSendPerMinute);
    var allowed = await rateLimiter.TryAcquireAsync(
        RateLimitKeys.SendMessage(channel.TenantId, profile.Id),
        sendLimit,
        TimeSpan.FromMinutes(1),
        ct);
    if (!allowed)
    {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    var hasAttachments = request.AttachmentIds is { Length: > 0 };
    if (request.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey) || (string.IsNullOrWhiteSpace(request.Body) && !hasAttachments))
    {
        return Results.BadRequest(new { error = "messageId, idempotencyKey and body or attachments are required." });
    }

    try
    {
        var result = await writer.SendAsync(new SendMessageCommand(
            channel.TenantId,
            profile.Id,
            channel.Id,
            new MessageId(request.MessageId),
            request.IdempotencyKey,
            request.Body ?? string.Empty,
            request.ReplyToMessageId is null ? null : new MessageId(request.ReplyToMessageId.Value),
            request.ThreadId,
            request.AttachmentIds), ct);

        var attachments = await db.Attachments.AsNoTracking()
            .Where(x => x.MessageId == result.MessageId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new AttachmentResponse(x.Id, x.FileName, x.ContentType, x.SizeBytes, x.Status.ToString()))
            .ToArrayAsync(ct);

        return Results.Accepted(
            $"/api/v1/channels/{channel.Id.Value}/messages?after={result.Sequence - 1}",
            new MessageResponse(
                result.MessageId.Value,
                channel.Id.Value,
                result.Sequence,
                profile.Id.Value,
                request.Body?.Trim() ?? string.Empty,
                result.CreatedAt,
                null,
                null,
                profile.DisplayName,
                attachments,
                request.ThreadId,
                request.ReplyToMessageId,
                0,
                channel.Id.Value));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
});

v1.MapPost("/channels/{channelId:guid}/messages/{messageId:guid}/threads", async (
    Guid channelId,
    Guid messageId,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var parent = await db.Messages.FirstOrDefaultAsync(
        x => x.Id == new MessageId(messageId) && x.ConversationId == channel.Id,
        ct);
    if (parent is null || parent.DeletedAt is not null)
    {
        return Results.NotFound();
    }

    MessageThread thread;
    if (parent.ThreadId is Guid existingThreadId)
    {
        thread = await db.MessageThreads.FirstAsync(x => x.Id == existingThreadId, ct);
    }
    else
    {
        var existing = await db.MessageThreads.FirstOrDefaultAsync(
            x => x.TenantId == channel.TenantId && x.ParentMessageId == parent.Id,
            ct);
        if (existing is not null)
        {
            thread = existing;
            parent.ThreadId = thread.Id;
        }
        else
        {
            thread = new MessageThread
            {
                Id = Guid.NewGuid(),
                TenantId = channel.TenantId,
                ChannelId = channel.Id,
                ParentMessageId = parent.Id,
                CreatedBy = profile.Id,
                CreatedAt = clock.UtcNow
            };
            db.MessageThreads.Add(thread);
            parent.ThreadId = thread.Id;
        }

        await db.SaveChangesAsync(ct);
    }

    var replyCount = await db.Messages.CountAsync(
        x => x.ThreadId == thread.Id && x.ConversationId == new ChannelId(thread.Id),
        ct);
    return Results.Ok(new ThreadResponse(
        thread.Id,
        thread.ChannelId.Value,
        thread.ParentMessageId.Value,
        thread.CreatedBy.Value,
        thread.CreatedAt,
        replyCount));
});

v1.MapGet("/threads/{threadId:guid}", async (
    Guid threadId,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == threadId, ct);
    if (thread is null)
    {
        return Results.NotFound();
    }

    var channel = await ResolveChannelAsync(thread.ChannelId, profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var parent = await (
        from m in db.Messages.AsNoTracking()
        where m.Id == thread.ParentMessageId
        join u in db.UserProfiles on m.AuthorId equals u.Id into authors
        from u in authors.DefaultIfEmpty()
        select new
        {
            Message = m,
            AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString()
        }).FirstOrDefaultAsync(ct);

    var replyCount = await db.Messages.CountAsync(
        x => x.ThreadId == thread.Id && x.ConversationId == new ChannelId(thread.Id),
        ct);

    MessageResponse? parentResponse = null;
    if (parent is not null)
    {
        var attachments = await LoadAttachmentsByMessageAsync(db, channel.Id, [parent.Message.Id], ct);
        var reactions = await LoadReactionSummariesByMessageAsync(db, [parent.Message.Id], profile.Id, ct);
        parentResponse = new MessageResponse(
            parent.Message.Id.Value,
            channel.Id.Value,
            parent.Message.Sequence,
            parent.Message.AuthorId.Value,
            parent.Message.DeletedAt == null ? parent.Message.Body : string.Empty,
            parent.Message.CreatedAt,
            parent.Message.EditedAt,
            parent.Message.DeletedAt,
            parent.AuthorName,
            parent.Message.DeletedAt == null && attachments.TryGetValue(parent.Message.Id.Value, out var atts) ? atts : [],
            thread.Id,
            parent.Message.ReplyToMessageId?.Value,
            replyCount,
            channel.Id.Value,
            parent.Message.DeletedAt == null && reactions.TryGetValue(parent.Message.Id.Value, out var rx) ? rx : []);
    }

    return Results.Ok(new ThreadResponse(
        thread.Id,
        thread.ChannelId.Value,
        thread.ParentMessageId.Value,
        thread.CreatedBy.Value,
        thread.CreatedAt,
        replyCount,
        parentResponse));
});

v1.MapGet("/threads/{threadId:guid}/messages", async (
    Guid threadId,
    long? after,
    int? limit,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == threadId, ct);
    if (thread is null)
    {
        return Results.NotFound();
    }

    var channel = await ResolveChannelAsync(thread.ChannelId, profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var conversationId = new ChannelId(thread.Id);
    var take = Math.Clamp(limit ?? 50, 1, 100);
    var rows = await (
        from m in db.Messages
        where m.ConversationId == conversationId && m.Sequence > (after ?? 0)
        join u in db.UserProfiles on m.AuthorId equals u.Id into authors
        from u in authors.DefaultIfEmpty()
        orderby m.Sequence
        select new
        {
            Id = m.Id,
            m.Sequence,
            m.AuthorId,
            m.Body,
            m.CreatedAt,
            m.EditedAt,
            m.DeletedAt,
            m.ThreadId,
            m.ReplyToMessageId,
            AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString()
        })
        .Take(take)
        .ToArrayAsync(ct);

    var messageIds = rows.Select(x => x.Id).ToArray();
    var attachmentsByMessage = await LoadAttachmentsByMessageAsync(db, channel.Id, messageIds, ct);
    var reactionsByMessage = await LoadReactionSummariesByMessageAsync(db, messageIds, profile.Id, ct);
    var messages = rows.Select(x => new MessageResponse(
        x.Id.Value,
        channel.Id.Value,
        x.Sequence,
        x.AuthorId.Value,
        x.DeletedAt == null ? x.Body : string.Empty,
        x.CreatedAt,
        x.EditedAt,
        x.DeletedAt,
        x.AuthorName,
        x.DeletedAt == null && attachmentsByMessage.TryGetValue(x.Id.Value, out var atts) ? atts : [],
        x.ThreadId ?? thread.Id,
        x.ReplyToMessageId?.Value,
        0,
        thread.Id,
        x.DeletedAt == null && reactionsByMessage.TryGetValue(x.Id.Value, out var rx) ? rx : [])).ToArray();
    return Results.Ok(messages);
});

v1.MapPost("/threads/{threadId:guid}/messages", async (
    Guid threadId,
    SendMessageRequest request,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IMessageWriter writer,
    IRateLimiter rateLimiter,
    IConfiguration config,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == threadId, ct);
    if (thread is null)
    {
        return Results.NotFound();
    }

    var channel = await ResolveChannelAsync(thread.ChannelId, profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var sendLimit = config.GetValue("RateLimit:SendPerMinute", RateLimitPolicies.DefaultSendPerMinute);
    var allowed = await rateLimiter.TryAcquireAsync(
        RateLimitKeys.SendMessage(channel.TenantId, profile.Id),
        sendLimit,
        TimeSpan.FromMinutes(1),
        ct);
    if (!allowed)
    {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    var hasAttachments = request.AttachmentIds is { Length: > 0 };
    if (request.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey) || (string.IsNullOrWhiteSpace(request.Body) && !hasAttachments))
    {
        return Results.BadRequest(new { error = "messageId, idempotencyKey and body or attachments are required." });
    }

    try
    {
        var result = await writer.SendAsync(new SendMessageCommand(
            channel.TenantId,
            profile.Id,
            channel.Id,
            new MessageId(request.MessageId),
            request.IdempotencyKey,
            request.Body ?? string.Empty,
            request.ReplyToMessageId is null ? new MessageId(thread.ParentMessageId.Value) : new MessageId(request.ReplyToMessageId.Value),
            thread.Id,
            request.AttachmentIds), ct);

        var attachments = await db.Attachments.AsNoTracking()
            .Where(x => x.MessageId == result.MessageId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new AttachmentResponse(x.Id, x.FileName, x.ContentType, x.SizeBytes, x.Status.ToString()))
            .ToArrayAsync(ct);

        return Results.Accepted(
            $"/api/v1/threads/{thread.Id}/messages?after={result.Sequence - 1}",
            new MessageResponse(
                result.MessageId.Value,
                channel.Id.Value,
                result.Sequence,
                profile.Id.Value,
                request.Body?.Trim() ?? string.Empty,
                result.CreatedAt,
                null,
                null,
                profile.DisplayName,
                attachments,
                thread.Id,
                request.ReplyToMessageId ?? thread.ParentMessageId.Value,
                0,
                thread.Id));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
});

v1.MapPost("/channels/{channelId:guid}/attachments", async (
    Guid channelId,
    CreateAttachmentUploadRequest request,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IObjectStorage storage,
    IConfiguration config,
    IAuditWriter audit,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    if (!await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Files.Upload, ct)
        || !await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.Send, ct))
    {
        return Results.Forbid();
    }

    var maxSize = config.GetValue("Files:MaxSizeBytes", AttachmentPolicies.DefaultMaxSizeBytes);
    var allowed = config.GetSection("Files:AllowedContentTypes").Get<string[]>() ?? AttachmentPolicies.DefaultAllowedContentTypes.ToArray();
    var uploadTtl = TimeSpan.FromSeconds(config.GetValue("Files:PresignUploadTtlSeconds", AttachmentPolicies.DefaultUploadTtlSeconds));

    if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.ContentType) || request.SizeBytes <= 0)
    {
        return Results.BadRequest(new { error = "fileName, contentType and sizeBytes are required." });
    }

    if (request.SizeBytes > maxSize)
    {
        return Results.BadRequest(new { error = $"File exceeds max size of {maxSize} bytes." });
    }

    if (!AttachmentPolicies.IsAllowedContentType(request.ContentType, allowed))
    {
        return Results.BadRequest(new { error = "Content type is not allowed." });
    }

    var attachmentId = Guid.NewGuid();
    var safeName = AttachmentPolicies.SanitizeFileName(request.FileName);
    var storageKey = AttachmentPolicies.BuildStorageKey(channel.TenantId, channel.Id, attachmentId, safeName);
    var now = clock.UtcNow;
    var attachment = new Attachment
    {
        Id = attachmentId,
        TenantId = channel.TenantId,
        ChannelId = channel.Id,
        UploadedBy = profile.Id,
        FileName = safeName,
        ContentType = request.ContentType.Trim(),
        SizeBytes = request.SizeBytes,
        StorageKey = storageKey,
        Status = AttachmentStatus.PendingUpload,
        CreatedAt = now
    };
    db.Attachments.Add(attachment);
    audit.Add(new AuditEvent
    {
        TenantId = channel.TenantId,
        ActorUserId = profile.Id,
        Action = AuditActions.AttachmentUpload,
        EntityType = "Attachment",
        EntityId = attachmentId.ToString(),
        MetadataJson = JsonSerializer.Serialize(new { channelId, safeName, request.ContentType, request.SizeBytes, stage = "initiate" })
    });
    await db.SaveChangesAsync(ct);

    var upload = await storage.CreateUploadUrlAsync(storageKey, attachment.ContentType, uploadTtl, ct);
    return Results.Ok(new AttachmentUploadResponse(
        attachment.Id,
        upload.Url.ToString(),
        upload.ExpiresAt,
        upload.RequiredHeaders,
        maxSize,
        attachment.FileName,
        attachment.ContentType));
});

v1.MapPost("/channels/{channelId:guid}/attachments/{attachmentId:guid}/complete", async (
    Guid channelId,
    Guid attachmentId,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IObjectStorage storage,
    IConfiguration config,
    IOutboxWriter outbox,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    if (!await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Files.Upload, ct))
    {
        return Results.Forbid();
    }

    var attachment = await db.Attachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.ChannelId == channel.Id, ct);
    if (attachment is null)
    {
        return Results.NotFound();
    }

    if (attachment.UploadedBy != profile.Id)
    {
        return Results.Forbid();
    }

    if (attachment.Status == AttachmentStatus.Ready)
    {
        return Results.Ok(new AttachmentResponse(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.Status.ToString()));
    }

    var stat = await storage.StatObjectAsync(attachment.StorageKey, ct);
    if (stat is null || stat.SizeBytes <= 0)
    {
        attachment.Status = AttachmentStatus.Failed;
        await db.SaveChangesAsync(ct);
        return Results.BadRequest(new { error = "Uploaded object was not found in storage." });
    }

    var maxSize = config.GetValue("Files:MaxSizeBytes", AttachmentPolicies.DefaultMaxSizeBytes);
    if (stat.SizeBytes > maxSize || stat.SizeBytes > attachment.SizeBytes)
    {
        attachment.Status = AttachmentStatus.Failed;
        await db.SaveChangesAsync(ct);
        return Results.BadRequest(new { error = "Uploaded object exceeds declared or allowed size." });
    }

    attachment.SizeBytes = stat.SizeBytes;
    attachment.Status = AttachmentStatus.Ready;
    attachment.ReadyAt = clock.UtcNow;
    attachment.ChecksumSha256 = string.IsNullOrWhiteSpace(stat.ETag) ? null : stat.ETag.Trim('"');

    outbox.Add(new OutboxMessage
    {
        TenantId = channel.TenantId,
        Type = "files.attachment.ready",
        Payload = JsonSerializer.Serialize(new
        {
            tenantId = channel.TenantId.Value,
            channelId,
            attachmentId = attachment.Id,
            fileName = attachment.FileName,
            contentType = attachment.ContentType,
            sizeBytes = attachment.SizeBytes,
            readyAt = attachment.ReadyAt
        })
    });
    await db.SaveChangesAsync(ct);
    return Results.Ok(new AttachmentResponse(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.Status.ToString()));
});

v1.MapGet("/channels/{channelId:guid}/attachments/{attachmentId:guid}/download", async (
    Guid channelId,
    Guid attachmentId,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IObjectStorage storage,
    IConfiguration config,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    if (!await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Files.Download, ct)
        || !await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.Read, ct))
    {
        return Results.Forbid();
    }

    var attachment = await db.Attachments.AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == attachmentId && x.ChannelId == channel.Id, ct);
    if (attachment is null || attachment.Status != AttachmentStatus.Ready)
    {
        return Results.NotFound();
    }

    var downloadTtl = TimeSpan.FromSeconds(config.GetValue("Files:PresignDownloadTtlSeconds", AttachmentPolicies.DefaultDownloadTtlSeconds));
    var download = await storage.CreateDownloadUrlAsync(attachment.StorageKey, attachment.FileName, downloadTtl, ct);
    return Results.Ok(new AttachmentDownloadResponse(
        attachment.Id,
        download.Url.ToString(),
        download.ExpiresAt,
        attachment.FileName,
        attachment.ContentType,
        attachment.SizeBytes));
});

v1.MapPut("/channels/{channelId:guid}/messages/{messageId:guid}", async (Guid channelId, Guid messageId, EditMessageRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IOutboxWriter outbox, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    if (string.IsNullOrWhiteSpace(request.Body))
    {
        return Results.BadRequest(new { error = "body is required." });
    }

    var message = await FindMessageInChannelAsync(db, channel.Id, new MessageId(messageId), ct);
    if (message is null || message.DeletedAt is not null)
    {
        return Results.NotFound();
    }

    var canEditOwn = message.AuthorId == profile.Id
        && await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.EditOwn, ct);
    if (!canEditOwn)
    {
        return Results.Forbid();
    }

    message.Body = request.Body.Trim();
    message.EditedAt = clock.UtcNow;
    outbox.Add(new OutboxMessage
    {
        TenantId = channel.TenantId,
        Type = nameof(MessageEditedEvent),
        Payload = JsonSerializer.Serialize(new
        {
            tenantId = channel.TenantId.Value,
            channelId,
            conversationId = message.ConversationId.Value,
            threadId = message.ThreadId,
            messageId,
            sequence = message.Sequence,
            body = message.Body,
            editedAt = message.EditedAt
        })
    });
    await db.SaveChangesAsync(ct);
    var attachments = await db.Attachments.AsNoTracking()
        .Where(x => x.MessageId == message.Id)
        .OrderBy(x => x.CreatedAt)
        .Select(x => new AttachmentResponse(x.Id, x.FileName, x.ContentType, x.SizeBytes, x.Status.ToString()))
        .ToArrayAsync(ct);
    return Results.Ok(new MessageResponse(
        message.Id.Value,
        channel.Id.Value,
        message.Sequence,
        message.AuthorId.Value,
        message.Body,
        message.CreatedAt,
        message.EditedAt,
        message.DeletedAt,
        profile.DisplayName,
        attachments,
        message.ThreadId,
        message.ReplyToMessageId?.Value,
        0,
        message.ConversationId.Value));
});

v1.MapPut("/channels/{channelId:guid}/messages/{messageId:guid}/reactions", async (
    Guid channelId,
    Guid messageId,
    ToggleReactionRequest request,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IOutboxWriter outbox,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    if (!await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.React, ct))
    {
        return Results.Forbid();
    }

    var emoji = request.Emoji?.Trim() ?? string.Empty;
    if (!ReactionEmojis.IsAllowed(emoji))
    {
        return Results.BadRequest(new { error = "emoji is not allowed." });
    }

    var message = await FindMessageInChannelAsync(db, channel.Id, new MessageId(messageId), ct);
    if (message is null || message.DeletedAt is not null)
    {
        return Results.NotFound();
    }

    var existing = await db.Reactions.FirstOrDefaultAsync(
        x => x.MessageId == message.Id && x.UserId == profile.Id && x.Emoji == emoji,
        ct);

    var added = existing is null;
    if (existing is null)
    {
        db.Reactions.Add(new Reaction
        {
            Id = Guid.NewGuid(),
            TenantId = channel.TenantId,
            MessageId = message.Id,
            UserId = profile.Id,
            Emoji = emoji,
            CreatedAt = clock.UtcNow
        });
    }
    else
    {
        db.Reactions.Remove(existing);
    }

    var snapshot = await BuildReactionSnapshotAsync(db, message.Id, profile.Id, emoji, added, ct);
    outbox.Add(new OutboxMessage
    {
        TenantId = channel.TenantId,
        Type = nameof(ReactionChangedEvent),
        Payload = JsonSerializer.Serialize(new
        {
            tenantId = channel.TenantId.Value,
            channelId,
            conversationId = message.ConversationId.Value,
            threadId = message.ThreadId,
            messageId,
            userId = profile.Id.Value,
            emoji,
            added,
            occurredAt = clock.UtcNow,
            reactions = snapshot.Select(x => new
            {
                emoji = x.Emoji,
                count = x.Count,
                userIds = x.UserIds
            })
        })
    });
    await db.SaveChangesAsync(ct);

    var summaries = snapshot
        .Select(x => new ReactionSummaryResponse(x.Emoji, x.Count, x.UserIds.Contains(profile.Id.Value)))
        .ToArray();
    return Results.Ok(new ToggleReactionResponse(messageId, channelId, emoji, added, summaries));
});

v1.MapDelete("/channels/{channelId:guid}/messages/{messageId:guid}", async (Guid channelId, Guid messageId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IOutboxWriter outbox, IAuditWriter audit, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var message = await FindMessageInChannelAsync(db, channel.Id, new MessageId(messageId), ct);
    if (message is null)
    {
        return Results.NotFound();
    }

    if (message.DeletedAt is not null)
    {
        return Results.NoContent();
    }

    var canDeleteOwn = message.AuthorId == profile.Id
        && await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.DeleteOwn, ct);
    var canDeleteAny = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.DeleteAny, ct);
    if (!canDeleteOwn && !canDeleteAny)
    {
        return Results.Forbid();
    }

    message.DeletedAt = clock.UtcNow;
    message.DeletedBy = profile.Id;
    outbox.Add(new OutboxMessage
    {
        TenantId = channel.TenantId,
        Type = nameof(MessageDeletedEvent),
        Payload = JsonSerializer.Serialize(new
        {
            tenantId = channel.TenantId.Value,
            channelId,
            conversationId = message.ConversationId.Value,
            threadId = message.ThreadId,
            messageId,
            sequence = message.Sequence,
            deletedAt = message.DeletedAt
        })
    });
    audit.Add(new AuditEvent { TenantId = channel.TenantId, ActorUserId = profile.Id, Action = AuditActions.MessageDelete, EntityType = "Message", EntityId = message.Id.ToString(), MetadataJson = JsonSerializer.Serialize(new { channelId, threadId = message.ThreadId, message.Sequence }) });
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

v1.MapPut("/channels/{channelId:guid}/read-cursor", async (Guid channelId, UpsertReadCursorRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IChatPublisher publisher, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var cursor = await db.ReadCursors.FirstOrDefaultAsync(x => x.ChannelId == channel.Id && x.UserId == profile.Id, ct);
    if (cursor is null)
    {
        cursor = new ReadCursor { Id = Guid.NewGuid(), TenantId = channel.TenantId, ChannelId = channel.Id, UserId = profile.Id };
        db.ReadCursors.Add(cursor);
    }

    cursor.LastReadSequence = request.LastReadSequence;
    cursor.UpdatedAt = clock.UtcNow;
    await db.SaveChangesAsync(ct);
    await publisher.PublishAsync(new RealtimeMessage("ReadCursorUpdated", channel.TenantId, channel.Id, new { tenantId = channel.TenantId.Value, channelId, userId = profile.Id.Value, cursor.LastReadSequence }), ct);
    return Results.Ok(new ReadCursorResponse(channel.Id.Value, profile.Id.Value, cursor.LastReadSequence, cursor.UpdatedAt));
});

v1.MapGet("/search/messages", async (
    Guid workspaceId,
    string? q,
    Guid? channelId,
    int? limit,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    ISearchQuery search,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null)
    {
        return Results.Forbid();
    }

    if (!await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Search.Messages, ct)
        || !await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Message.Read, ct))
    {
        return Results.Forbid();
    }

    ChannelId? scopedChannel = null;
    if (channelId is not null)
    {
        var channel = await ResolveChannelAsync(new ChannelId(channelId.Value), profile.Id, db, tenant, ct);
        if (channel is null || channel.WorkspaceId != workspace.Id)
        {
            return Results.Forbid();
        }

        scopedChannel = channel.Id;
    }

    try
    {
        var page = await search.SearchMessagesAsync(
            new SearchMessagesQuery(
                workspace.TenantId,
                profile.Id,
                workspace.Id,
                q ?? string.Empty,
                scopedChannel,
                SearchPolicies.NormalizeLimit(limit)),
            ct);

        return Results.Ok(new SearchMessagesResponse(
            page.Query,
            page.Limit,
            page.Items.Select(x => new SearchMessageHitResponse(
                x.MessageId,
                x.ChannelId,
                x.ChannelName,
                x.ChannelType,
                x.Sequence,
                x.AuthorUserId,
                x.AuthorDisplayName,
                x.BodyPreview,
                x.CreatedAt,
                x.Rank)).ToArray()));
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.GetBaseException().Message, statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireAuthorization();

v1.MapGet("/channels/{channelId:guid}/unread-count", async (Guid channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var lastRead = await db.ReadCursors.Where(x => x.ChannelId == channel.Id && x.UserId == profile.Id).Select(x => (long?)x.LastReadSequence).FirstOrDefaultAsync(ct) ?? 0;
    var count = await db.Messages.CountAsync(x => x.ConversationId == channel.Id && x.Sequence > lastRead && x.DeletedAt == null, ct);
    return Results.Ok(new { channelId, unreadCount = count });
});

v1.MapGet("/admin/dashboard", async (HttpContext http, VibeChatDbContext db, IDashboardQuery dashboard, IPresenceService presence, HealthCheckService health, IConfiguration config, IClock clock, CancellationToken ct) =>
{
    await EnsureProfileAsync(http.User, db, clock, ct);
    var stats = await dashboard.GetStatsAsync(ct);
    var online = await presence.CountOnlineAsync(SeedData.DemoTenantId, ct);
    var failures = await db.OutboxMessages.IgnoreQueryFilters().CountAsync(x => x.ProcessedAt == null && x.Attempts > 0, ct);
    var report = await health.CheckHealthAsync(ct);
    string MapHealth(string name) => report.Entries.TryGetValue(name, out var entry)
        ? entry.Status switch
        {
            HealthStatus.Healthy => "up",
            HealthStatus.Degraded => "degraded",
            _ => "down"
        }
        : "down";

    return Results.Ok(new AdminDashboardResponse(
        stats.UserCount,
        online,
        stats.WorkspaceCount,
        stats.ChannelCount,
        stats.MessageCount,
        (int)Math.Max(0, VibeChatMetrics.RealtimeConnectionsGauge),
        stats.PendingOutboxCount,
        failures,
        new AdminHealthResponse(MapHealth("postgres"), MapHealth("redis"), MapHealth("minio")),
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0",
        config["Observability:GrafanaUrl"] ?? "http://localhost:3000"));
});

v1.MapGet("/admin/audit-events", async (
    int? limit,
    string? action,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IClock clock,
    CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var membership = await db.WorkspaceMembers.IgnoreQueryFilters()
        .AsNoTracking()
        .Where(x => x.UserId == profile.Id)
        .OrderBy(x => x.JoinedAt)
        .FirstOrDefaultAsync(ct);
    if (membership is null
        || !await permissions.HasPermissionAsync(membership.TenantId, profile.Id, Permissions.Admin.Dashboard, ct))
    {
        return Results.Forbid();
    }

    tenant.SetTenant(membership.TenantId);
    var take = Math.Clamp(limit ?? 50, 1, 200);
    var query = db.AuditEvents.AsNoTracking().Where(x => x.TenantId == membership.TenantId);
    if (!string.IsNullOrWhiteSpace(action))
    {
        query = query.Where(x => x.Action == action);
    }

    var rows = await query
        .OrderByDescending(x => x.OccurredAt)
        .Take(take)
        .ToListAsync(ct);

    var items = rows.Select(x => new AuditEventResponse(
        x.Id,
        x.Action,
        x.EntityType,
        x.EntityId,
        x.ActorUserId?.Value,
        x.OccurredAt,
        x.MetadataJson)).ToArray();

    return Results.Ok(new AuditEventsResponse(items));
});

// B-067: conversation audit viewer — admin.dashboard; bypass channel membership within tenant.
v1.MapGet("/admin/conversations", async (
    Guid? workspaceId,
    int? limit,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IClock clock,
    CancellationToken ct) =>
{
    var access = await ResolveAdminDashboardAccessAsync(http, db, tenant, permissions, clock, ct);
    if (access is null)
    {
        return Results.Forbid();
    }

    var (profile, tenantId) = access.Value;
    var take = Math.Clamp(limit ?? 100, 1, 200);
    var query = db.Channels.IgnoreQueryFilters().AsNoTracking()
        .Where(x => x.TenantId == tenantId);
    if (workspaceId is { } wsId && wsId != Guid.Empty)
    {
        var workspaceKey = new WorkspaceId(wsId);
        var workspaceOk = await db.Workspaces.IgnoreQueryFilters()
            .AnyAsync(x => x.Id == workspaceKey && x.TenantId == tenantId, ct);
        if (!workspaceOk)
        {
            return Results.Forbid();
        }

        query = query.Where(x => x.WorkspaceId == workspaceKey);
    }

    var channels = await query
        .OrderBy(x => x.Type == ChannelType.Direct ? 1 : 0)
        .ThenBy(x => x.Name)
        .Take(take)
        .ToListAsync(ct);

    var peerByChannel = await ResolveDirectPeersAsync(channels, profile.Id, db, ct);
    var items = channels.Select(x =>
    {
        peerByChannel.TryGetValue(x.Id, out var peer);
        var displayName = x.Type == ChannelType.Direct && peer is not null ? peer.DisplayName : x.Name;
        return new AdminConversationResponse(
            x.Id.Value,
            x.WorkspaceId.Value,
            displayName,
            x.Type.ToString(),
            x.SpaceId,
            peer?.UserId.Value,
            peer?.DisplayName);
    }).ToArray();

    return Results.Ok(new AdminConversationsResponse(items));
});

v1.MapGet("/admin/conversations/{channelId:guid}/messages", async (
    Guid channelId,
    long? after,
    int? limit,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IClock clock,
    CancellationToken ct) =>
{
    var access = await ResolveAdminDashboardAccessAsync(http, db, tenant, permissions, clock, ct);
    if (access is null)
    {
        return Results.Forbid();
    }

    var (_, tenantId) = access.Value;
    var channelKey = new ChannelId(channelId);
    var channel = await db.Channels.IgnoreQueryFilters().AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == channelKey && x.TenantId == tenantId, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var take = Math.Clamp(limit ?? 50, 1, 200);
    var rows = await (
        from m in db.Messages.IgnoreQueryFilters().AsNoTracking()
        where m.TenantId == tenantId && m.ConversationId == channel.Id && m.Sequence > (after ?? 0)
        join u in db.UserProfiles.IgnoreQueryFilters().AsNoTracking() on m.AuthorId equals u.Id into authors
        from u in authors.DefaultIfEmpty()
        join d in db.UserProfiles.IgnoreQueryFilters().AsNoTracking() on m.DeletedBy equals d.Id into deleters
        from d in deleters.DefaultIfEmpty()
        orderby m.Sequence
        select new
        {
            m.Id,
            ChannelId = channel.Id,
            ConversationId = m.ConversationId,
            m.Sequence,
            m.AuthorId,
            m.Body,
            m.CreatedAt,
            m.EditedAt,
            m.DeletedAt,
            m.DeletedBy,
            m.ThreadId,
            m.ReplyToMessageId,
            AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString(),
            DeletedByName = d != null ? d.DisplayName : null
        })
        .Take(take)
        .ToArrayAsync(ct);

    var messageIds = rows.Select(x => x.Id).ToArray();
    var threadIds = rows.Where(x => x.ThreadId is not null).Select(x => x.ThreadId!.Value).Distinct().ToArray();
    var replyCounts = threadIds.Length == 0
        ? new Dictionary<Guid, int>()
        : await db.Messages.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.TenantId == tenantId
                && m.ThreadId != null
                && threadIds.Contains(m.ThreadId.Value)
                && m.ConversationId != channel.Id)
            .GroupBy(m => m.ThreadId!.Value)
            .Select(g => new { ThreadId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ThreadId, x => x.Count, ct);

    var attachmentsByMessage = await LoadAttachmentsByMessageAsync(db, channel.Id, messageIds, ct);
    var items = rows.Select(x => new AdminConversationMessageResponse(
        x.Id.Value,
        x.ChannelId.Value,
        x.ConversationId.Value,
        x.Sequence,
        x.AuthorId.Value,
        x.AuthorName,
        x.Body,
        x.CreatedAt,
        x.EditedAt,
        x.DeletedAt,
        x.DeletedBy?.Value,
        x.DeletedByName,
        x.ThreadId,
        x.ReplyToMessageId?.Value,
        x.ThreadId is Guid tid && replyCounts.TryGetValue(tid, out var count) ? count : 0,
        attachmentsByMessage.TryGetValue(x.Id.Value, out var atts) ? atts : [])).ToArray();

    return Results.Ok(new AdminConversationMessagesResponse(items));
});

v1.MapGet("/admin/threads/{threadId:guid}/messages", async (
    Guid threadId,
    long? after,
    int? limit,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IClock clock,
    CancellationToken ct) =>
{
    var access = await ResolveAdminDashboardAccessAsync(http, db, tenant, permissions, clock, ct);
    if (access is null)
    {
        return Results.Forbid();
    }

    var (_, tenantId) = access.Value;
    var thread = await db.MessageThreads.IgnoreQueryFilters().AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == threadId && x.TenantId == tenantId, ct);
    if (thread is null)
    {
        return Results.Forbid();
    }

    var conversationId = new ChannelId(thread.Id);
    var take = Math.Clamp(limit ?? 50, 1, 200);
    var rows = await (
        from m in db.Messages.IgnoreQueryFilters().AsNoTracking()
        where m.TenantId == tenantId && m.ConversationId == conversationId && m.Sequence > (after ?? 0)
        join u in db.UserProfiles.IgnoreQueryFilters().AsNoTracking() on m.AuthorId equals u.Id into authors
        from u in authors.DefaultIfEmpty()
        join d in db.UserProfiles.IgnoreQueryFilters().AsNoTracking() on m.DeletedBy equals d.Id into deleters
        from d in deleters.DefaultIfEmpty()
        orderby m.Sequence
        select new
        {
            m.Id,
            ChannelId = thread.ChannelId,
            ConversationId = m.ConversationId,
            m.Sequence,
            m.AuthorId,
            m.Body,
            m.CreatedAt,
            m.EditedAt,
            m.DeletedAt,
            m.DeletedBy,
            m.ThreadId,
            m.ReplyToMessageId,
            AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString(),
            DeletedByName = d != null ? d.DisplayName : null
        })
        .Take(take)
        .ToArrayAsync(ct);

    var messageIds = rows.Select(x => x.Id).ToArray();
    var attachmentsByMessage = await LoadAttachmentsByMessageAsync(db, thread.ChannelId, messageIds, ct);
    var items = rows.Select(x => new AdminConversationMessageResponse(
        x.Id.Value,
        x.ChannelId.Value,
        x.ConversationId.Value,
        x.Sequence,
        x.AuthorId.Value,
        x.AuthorName,
        x.Body,
        x.CreatedAt,
        x.EditedAt,
        x.DeletedAt,
        x.DeletedBy?.Value,
        x.DeletedByName,
        x.ThreadId ?? thread.Id,
        x.ReplyToMessageId?.Value,
        0,
        attachmentsByMessage.TryGetValue(x.Id.Value, out var atts) ? atts : [])).ToArray();

    return Results.Ok(new AdminConversationMessagesResponse(items));
});

// B-069: sensitive integration settings — admin-only, secrets always masked.
v1.MapGet("/admin/settings", async (
    Guid? workspaceId,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IConfiguration config,
    EmailSettingsResolver emailSettings,
    IClock clock,
    CancellationToken ct) =>
{
    var access = await ResolveSensitiveSettingsAccessAsync(http, db, tenant, permissions, workspaceId, clock, ct);
    if (access is null)
    {
        return Results.Forbid();
    }

    var (_, workspace) = access.Value;
    return Results.Ok(await BuildSensitiveSettingsResponseAsync(workspace, db, config, emailSettings, ct));
});

v1.MapPut("/admin/settings", async (
    UpdateSensitiveSettingsRequest request,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IConfiguration config,
    IAuditWriter audit,
    EmailSettingsResolver emailSettings,
    IClock clock,
    CancellationToken ct) =>
{
    var access = await ResolveSensitiveSettingsAccessAsync(
        http, db, tenant, permissions, request.WorkspaceId, clock, ct);
    if (access is null)
    {
        return Results.Forbid();
    }

    var (profile, workspace) = access.Value;

    // AI/SMTP secrets are env/secret-store only (ADR-012 / B-069).
    // Webhook signing secret is the exception (B-048): shared with the consumer via admin API.
    if (request.Ai?.ApiKey is not null || request.Email?.SmtpPassword is not null)
    {
        return Results.BadRequest(new
        {
            error = "SecretsNotWritable",
            message = "API keys and SMTP passwords are configured via environment / secret store only."
        });
    }

    var changes = new List<string>();

    if (request.Ai is not null)
    {
        var aiSettings = await db.AiSettings
            .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId && x.WorkspaceId == workspace.Id, ct);
        if (aiSettings is null)
        {
            aiSettings = new AiSettings
            {
                WorkspaceId = workspace.Id,
                TenantId = workspace.TenantId,
                Enabled = false,
                Provider = "Mock"
            };
            db.AiSettings.Add(aiSettings);
            changes.Add("ai.created");
        }

        if (request.Ai.WorkspaceEnabled is { } workspaceEnabled && aiSettings.Enabled != workspaceEnabled)
        {
            aiSettings.Enabled = workspaceEnabled;
            changes.Add("ai.workspaceEnabled");
        }

        if (!string.IsNullOrWhiteSpace(request.Ai.Provider))
        {
            var provider = request.Ai.Provider.Trim();
            if (!string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(provider, "OpenRouter", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "InvalidAiProvider", message = "Provider must be Mock or OpenRouter." });
            }

            var normalized = string.Equals(provider, "OpenRouter", StringComparison.OrdinalIgnoreCase) ? "OpenRouter" : "Mock";
            if (!string.Equals(aiSettings.Provider, normalized, StringComparison.Ordinal))
            {
                aiSettings.Provider = normalized;
                changes.Add("ai.provider");
            }
        }
    }

    if (request.Email is not null)
    {
        var emailRow = await db.TenantEmailSettings
            .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
        var created = false;
        if (emailRow is null)
        {
            var baseline = await emailSettings.ResolveAsync(workspace.TenantId, ct);
            emailRow = new TenantEmailSettings
            {
                TenantId = workspace.TenantId,
                Enabled = baseline.Enabled,
                Host = baseline.Host,
                Port = baseline.Port,
                Username = baseline.Username,
                From = baseline.From,
                UseStartTls = baseline.UseStartTls,
                UpdatedAt = clock.UtcNow
            };
            db.TenantEmailSettings.Add(emailRow);
            created = true;
            changes.Add("email.created");
        }

        if (request.Email.Enabled is { } emailEnabled && emailRow.Enabled != emailEnabled)
        {
            emailRow.Enabled = emailEnabled;
            changes.Add("email.enabled");
        }

        if (request.Email.SmtpHost is not null)
        {
            var host = request.Email.SmtpHost.Trim();
            if (host.Length > 256)
            {
                return Results.BadRequest(new { error = "InvalidSmtpHost" });
            }

            if (!string.Equals(emailRow.Host, host, StringComparison.Ordinal))
            {
                emailRow.Host = host;
                changes.Add("email.smtpHost");
            }
        }

        if (request.Email.SmtpPort is { } port)
        {
            if (port is < 1 or > 65535)
            {
                return Results.BadRequest(new { error = "InvalidSmtpPort" });
            }

            if (emailRow.Port != port)
            {
                emailRow.Port = port;
                changes.Add("email.smtpPort");
            }
        }

        if (request.Email.SmtpUsername is not null)
        {
            var username = request.Email.SmtpUsername.Trim();
            if (username.Length > 256)
            {
                return Results.BadRequest(new { error = "InvalidSmtpUsername" });
            }

            if (!string.Equals(emailRow.Username, username, StringComparison.Ordinal))
            {
                emailRow.Username = username;
                changes.Add("email.smtpUsername");
            }
        }

        if (request.Email.SmtpFrom is not null)
        {
            var from = request.Email.SmtpFrom.Trim();
            if (from.Length > 320)
            {
                return Results.BadRequest(new { error = "InvalidSmtpFrom" });
            }

            if (!string.Equals(emailRow.From, from, StringComparison.Ordinal))
            {
                emailRow.From = from;
                changes.Add("email.smtpFrom");
            }
        }

        if (request.Email.UseStartTls is { } tls && emailRow.UseStartTls != tls)
        {
            emailRow.UseStartTls = tls;
            changes.Add("email.useStartTls");
        }

        if (created || changes.Any(c => c.StartsWith("email.", StringComparison.Ordinal)))
        {
            emailRow.UpdatedAt = clock.UtcNow;
        }
    }

    if (request.Webhooks is not null)
    {
        var webhookRow = await db.OutboundWebhookEndpoints
            .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
        var created = false;
        if (webhookRow is null)
        {
            webhookRow = new OutboundWebhookEndpoint
            {
                TenantId = workspace.TenantId,
                Enabled = false,
                Url = string.Empty,
                Secret = string.Empty,
                UpdatedAt = clock.UtcNow
            };
            db.OutboundWebhookEndpoints.Add(webhookRow);
            created = true;
            changes.Add("webhooks.created");
        }

        if (request.Webhooks.Url is not null)
        {
            var url = request.Webhooks.Url.Trim();
            if (url.Length > 2048)
            {
                return Results.BadRequest(new { error = "InvalidWebhookUrl", message = "URL exceeds 2048 characters." });
            }

            if (url.Length > 0 && !WebhookDelivery.IsValidHttpsUrl(url))
            {
                return Results.BadRequest(new
                {
                    error = "InvalidWebhookUrl",
                    message = "URL must be https (or http://localhost for lab)."
                });
            }

            if (!string.Equals(webhookRow.Url, url, StringComparison.Ordinal))
            {
                webhookRow.Url = url;
                changes.Add("webhooks.url");
            }
        }

        if (request.Webhooks.Secret is not null)
        {
            var secret = request.Webhooks.Secret.Trim();
            if (secret.Length > 512)
            {
                return Results.BadRequest(new { error = "InvalidWebhookSecret", message = "Secret exceeds 512 characters." });
            }

            if (secret.Length > 0 && secret.Length < 8)
            {
                return Results.BadRequest(new { error = "InvalidWebhookSecret", message = "Secret must be at least 8 characters." });
            }

            // Empty string means "keep existing"; only rotate when a non-empty value is provided.
            if (secret.Length > 0 && !string.Equals(webhookRow.Secret, secret, StringComparison.Ordinal))
            {
                webhookRow.Secret = secret;
                changes.Add("webhooks.secret");
            }
        }

        if (request.Webhooks.Enabled is { } webhookEnabled && webhookRow.Enabled != webhookEnabled)
        {
            if (webhookEnabled
                && (!SecretMasking.IsConfigured(webhookRow.Url) || !SecretMasking.IsConfigured(webhookRow.Secret)))
            {
                return Results.BadRequest(new
                {
                    error = "WebhookIncomplete",
                    message = "Enable requires a valid URL and signing secret."
                });
            }

            webhookRow.Enabled = webhookEnabled;
            changes.Add("webhooks.enabled");
        }

        if (created || changes.Any(c => c.StartsWith("webhooks.", StringComparison.Ordinal)))
        {
            webhookRow.UpdatedAt = clock.UtcNow;
        }
    }

    if (changes.Count > 0)
    {
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.SettingsChange,
            EntityType = "SensitiveSettings",
            EntityId = workspace.Id.Value.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                workspaceId = workspace.Id.Value,
                changes
            })
        });
        await db.SaveChangesAsync(ct);
    }

    return Results.Ok(await BuildSensitiveSettingsResponseAsync(workspace, db, config, emailSettings, ct));
});

// B-046: workspace compliance export (ZIP of JSON) — workspace.admin only (not Auditor).
v1.MapGet("/admin/workspaces/{workspaceId:guid}/export", async (
    Guid workspaceId,
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IAuditWriter audit,
    IClock clock,
    CancellationToken ct) =>
{
    var access = await ResolveSensitiveSettingsAccessAsync(
        http, db, tenant, permissions, workspaceId, clock, ct);
    if (access is null)
    {
        return Results.Forbid();
    }

    var (profile, workspace) = access.Value;
    var exportedAt = clock.UtcNow;
    var zipBytes = await BuildWorkspaceExportZipAsync(workspace, db, profile.Id, exportedAt, ct);

    audit.Add(new AuditEvent
    {
        TenantId = workspace.TenantId,
        ActorUserId = profile.Id,
        Action = AuditActions.WorkspaceExport,
        EntityType = "Workspace",
        EntityId = workspace.Id.Value.ToString(),
        MetadataJson = JsonSerializer.Serialize(new
        {
            workspaceId = workspace.Id.Value,
            byteLength = zipBytes.Length
        })
    });
    await db.SaveChangesAsync(ct);

    var fileName = $"vibechat-export-{workspace.Slug}-{exportedAt:yyyyMMddHHmmss}.zip";
    return Results.File(zipBytes, "application/zip", fileName);
});

v1.MapGet("/admin/health-summary", async (HealthCheckService health, CancellationToken ct) =>
{
    var report = await health.CheckHealthAsync(ct);
    return Results.Ok(new { status = report.Status.ToString(), checks = report.Entries.ToDictionary(x => x.Key, x => x.Value.Status.ToString()) });
});

v1.MapGet("/admin/version", () => Results.Ok(new { name = "VibeChat.Api", version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0" }));

v1.MapPost("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/summarize", async (Guid workspaceId, Guid channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, ISummarizeChannelFeature summarize, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null || !await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Ai.Summarize, ct))
    {
        return Results.Forbid();
    }

    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null || channel.WorkspaceId != workspace.Id)
    {
        return Results.Forbid();
    }

    // Never on SendMessage hot path — opt-in summarize only (D-06 / ADR-012).
    var result = await summarize.SummarizeAsync(workspace.TenantId, workspace.Id, channel.Id, ct);
    if (!result.Ok)
    {
        var status = string.Equals(result.Error, "AiDisabled", StringComparison.Ordinal)
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status502BadGateway;
        return Results.Json(new AiSummaryErrorResponse(result.Error ?? "AiError", result.Summary), statusCode: status);
    }

    return Results.Ok(new AiSummaryResponse(result.Summary));
});

v1.MapPost("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/suggest-reply", async (Guid workspaceId, Guid channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, ISuggestChannelReplyFeature suggestReply, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null || !await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Ai.SuggestReply, ct))
    {
        return Results.Forbid();
    }

    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null || channel.WorkspaceId != workspace.Id)
    {
        return Results.Forbid();
    }

    // Never on SendMessage hot path — opt-in suggest-reply only (D-06 / ADR-012 / B-045).
    var result = await suggestReply.SuggestAsync(workspace.TenantId, workspace.Id, channel.Id, ct);
    if (!result.Ok)
    {
        var status = string.Equals(result.Error, "AiDisabled", StringComparison.Ordinal)
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status502BadGateway;
        return Results.Json(new AiSummaryErrorResponse(result.Error ?? "AiError", result.Suggestion), statusCode: status);
    }

    return Results.Ok(new AiSuggestReplyResponse(result.Suggestion));
});

if (app.Environment.IsDevelopment())
{
    v1.MapPost("/dev/seed", async (SeedData seed, CancellationToken ct) =>
    {
        await seed.SeedAsync(ct);
        return Results.Ok(new { seeded = true });
    }).AllowAnonymous();
}

app.MapHub<ChatHub>("/hubs/chat").RequireAuthorization();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/ready"); // ops alias (runbooks / Compose)

app.Run();

static async Task<(UserProfile Profile, TenantId TenantId)?> ResolveAdminDashboardAccessAsync(
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    IClock clock,
    CancellationToken ct)
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var membership = await db.WorkspaceMembers.IgnoreQueryFilters()
        .AsNoTracking()
        .Where(x => x.UserId == profile.Id)
        .OrderBy(x => x.JoinedAt)
        .FirstOrDefaultAsync(ct);
    if (membership is null
        || !await permissions.HasPermissionAsync(membership.TenantId, profile.Id, Permissions.Admin.Dashboard, ct))
    {
        return null;
    }

    tenant.SetTenant(membership.TenantId);
    return (profile, membership.TenantId);
}

static async Task<(UserProfile Profile, Workspace Workspace)?> ResolveSensitiveSettingsAccessAsync(
    HttpContext http,
    VibeChatDbContext db,
    ITenantContext tenant,
    IPermissionChecker permissions,
    Guid? workspaceId,
    IClock clock,
    CancellationToken ct)
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);

    Workspace? workspace = null;
    if (workspaceId is { } id && id != Guid.Empty)
    {
        workspace = await ResolveWorkspaceAsync(new WorkspaceId(id), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return null;
        }
    }
    else
    {
        var membership = await db.WorkspaceMembers.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.UserId == profile.Id)
            .OrderBy(x => x.JoinedAt)
            .FirstOrDefaultAsync(ct);
        if (membership is null)
        {
            return null;
        }

        workspace = await db.Workspaces.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == membership.WorkspaceId, ct);
        if (workspace is null)
        {
            return null;
        }

        tenant.SetTenant(workspace.TenantId);
    }

    // B-069 / B-046: sensitive settings and workspace export are workspace-admin only —
    // Auditor (admin.dashboard) may view conversation audit (B-067) but must not export
    // or read/alter AI/SMTP integration flags.
    var canWorkspaceAdmin = await permissions.HasPermissionAsync(
        workspace.TenantId, profile.Id, Permissions.Workspace.Admin, ct);
    if (!canWorkspaceAdmin)
    {
        return null;
    }

    return (profile, workspace);
}

static async Task<byte[]> BuildWorkspaceExportZipAsync(
    Workspace workspace,
    VibeChatDbContext db,
    UserId actorUserId,
    DateTimeOffset exportedAt,
    CancellationToken ct)
{
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    var members = await (
        from m in db.WorkspaceMembers.AsNoTracking()
        join u in db.UserProfiles.AsNoTracking() on m.UserId equals u.Id
        where m.TenantId == workspace.TenantId && m.WorkspaceId == workspace.Id
        orderby m.JoinedAt
        select new
        {
            userId = m.UserId.Value,
            displayName = u.DisplayName,
            email = u.Email,
            role = m.Role.ToString(),
            joinedAt = m.JoinedAt
        }).ToListAsync(ct);

    var spaces = await db.Spaces.AsNoTracking()
        .Where(x => x.TenantId == workspace.TenantId && x.WorkspaceId == workspace.Id)
        .OrderBy(x => x.Order)
        .Select(x => new
        {
            id = x.Id,
            name = x.Name,
            order = x.Order,
            createdAt = x.CreatedAt
        })
        .ToListAsync(ct);

    var channels = await db.Channels.AsNoTracking()
        .Where(x => x.TenantId == workspace.TenantId && x.WorkspaceId == workspace.Id)
        .OrderBy(x => x.CreatedAt)
        .Select(x => new
        {
            id = x.Id.Value,
            name = x.Name,
            type = x.Type.ToString(),
            spaceId = x.SpaceId,
            createdAt = x.CreatedAt,
            createdBy = x.CreatedBy.Value
        })
        .ToListAsync(ct);

    var channelIds = channels.Select(c => new ChannelId(c.id)).ToList();

    var threads = await db.MessageThreads.AsNoTracking()
        .Where(x => x.TenantId == workspace.TenantId && channelIds.Contains(x.ChannelId))
        .OrderBy(x => x.CreatedAt)
        .Select(x => new
        {
            id = x.Id,
            channelId = x.ChannelId.Value,
            parentMessageId = x.ParentMessageId.Value,
            createdBy = x.CreatedBy.Value,
            createdAt = x.CreatedAt
        })
        .ToListAsync(ct);

    // Messages in root use ConversationId = channel; replies use ConversationId = threadId.
    // Include soft-deleted bodies (compliance parity with B-067). Attachment binaries omitted.
    var threadConversationIds = threads.Select(t => new ChannelId(t.id)).ToList();
    var messageEntities = await db.Messages.AsNoTracking()
        .Where(x => x.TenantId == workspace.TenantId
            && (channelIds.Contains(x.ConversationId) || threadConversationIds.Contains(x.ConversationId)))
        .OrderBy(x => x.CreatedAt)
        .ThenBy(x => x.Sequence)
        .ToListAsync(ct);

    var allMessages = messageEntities
        .Select(x => new
        {
            id = x.Id.Value,
            conversationId = x.ConversationId.Value,
            sequence = x.Sequence,
            authorId = x.AuthorId.Value,
            body = x.Body,
            replyToMessageId = x.ReplyToMessageId?.Value,
            threadId = x.ThreadId,
            createdAt = x.CreatedAt,
            editedAt = x.EditedAt,
            deletedAt = x.DeletedAt,
            deletedBy = x.DeletedBy?.Value
        })
        .ToList();

    var messageIds = allMessages.Select(m => new MessageId(m.id)).ToList();
    var attachmentEntities = await db.Attachments.AsNoTracking()
        .Where(x => x.TenantId == workspace.TenantId && x.MessageId != null && messageIds.Contains(x.MessageId.Value))
        .OrderBy(x => x.CreatedAt)
        .ToListAsync(ct);

    var attachments = attachmentEntities
        .Select(x => new
        {
            id = x.Id,
            messageId = x.MessageId!.Value.Value,
            channelId = x.ChannelId.Value,
            fileName = x.FileName,
            contentType = x.ContentType,
            sizeBytes = x.SizeBytes,
            status = x.Status.ToString(),
            checksumSha256 = x.ChecksumSha256,
            createdAt = x.CreatedAt
        })
        .ToList();

    var manifest = new
    {
        format = "vibechat.workspace.export.v1",
        tenantId = workspace.TenantId.Value,
        workspaceId = workspace.Id.Value,
        workspaceSlug = workspace.Slug,
        exportedAt,
        actorUserId = actorUserId.Value,
        counts = new
        {
            members = members.Count,
            spaces = spaces.Count,
            channels = channels.Count,
            threads = threads.Count,
            messages = allMessages.Count,
            attachments = attachments.Count
        }
    };

    var workspacePayload = new
    {
        id = workspace.Id.Value,
        tenantId = workspace.TenantId.Value,
        name = workspace.Name,
        slug = workspace.Slug,
        aiEnabled = workspace.AiEnabled,
        createdAt = workspace.CreatedAt
    };

    await using var memory = new MemoryStream();
    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
    {
        await WriteZipJsonEntryAsync(zip, "manifest.json", manifest, jsonOptions, ct);
        await WriteZipJsonEntryAsync(zip, "workspace.json", workspacePayload, jsonOptions, ct);
        await WriteZipJsonEntryAsync(zip, "members.json", members, jsonOptions, ct);
        await WriteZipJsonEntryAsync(zip, "spaces.json", spaces, jsonOptions, ct);
        await WriteZipJsonEntryAsync(zip, "channels.json", channels, jsonOptions, ct);
        await WriteZipJsonEntryAsync(zip, "threads.json", threads, jsonOptions, ct);
        await WriteZipJsonEntryAsync(zip, "messages.json", allMessages, jsonOptions, ct);
        await WriteZipJsonEntryAsync(zip, "attachments.json", attachments, jsonOptions, ct);
    }

    return memory.ToArray();
}

static async Task WriteZipJsonEntryAsync<T>(
    ZipArchive zip,
    string entryName,
    T payload,
    JsonSerializerOptions jsonOptions,
    CancellationToken ct)
{
    var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
    await using var stream = entry.Open();
    await JsonSerializer.SerializeAsync(stream, payload, jsonOptions, ct);
}

static async Task<SensitiveSettingsResponse> BuildSensitiveSettingsResponseAsync(
    Workspace workspace,
    VibeChatDbContext db,
    IConfiguration config,
    EmailSettingsResolver emailSettings,
    CancellationToken ct)
{
    var processAiEnabled = config.GetValue("Ai:Enabled", false);
    var processAiProvider = config["Ai:Provider"] ?? "Mock";
    var apiKey = config["Ai:OpenRouter:ApiKey"] ?? config["OPENROUTER_API_KEY"];
    var aiWorkspace = await db.AiSettings.AsNoTracking()
        .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId && x.WorkspaceId == workspace.Id, ct);

    var smtp = await emailSettings.ResolveAsync(workspace.TenantId, ct);
    var envPassword = config["Email:Smtp:Password"] ?? config["SMTP_PASSWORD"];

    var webhook = await db.OutboundWebhookEndpoints.AsNoTracking()
        .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
    var webhookUrl = webhook?.Url ?? string.Empty;
    var webhookSecret = webhook?.Secret ?? string.Empty;
    var webhookUrlConfigured = SecretMasking.IsConfigured(webhookUrl);
    var webhookSecretConfigured = SecretMasking.IsConfigured(webhookSecret);
    var webhookEnabled = webhook?.Enabled ?? false;
    var webhookStatus = WebhooksSettingsStatus.Resolve(webhookEnabled, webhookUrlConfigured, webhookSecretConfigured);

    return new SensitiveSettingsResponse(
        workspace.Id.Value,
        new AiSensitiveSettingsResponse(
            processAiEnabled,
            "env",
            aiWorkspace?.Enabled ?? false,
            aiWorkspace?.Provider ?? processAiProvider,
            SecretMasking.IsConfigured(apiKey),
            SecretMasking.Mask(apiKey),
            SecretsWritable: false),
        new EmailSensitiveSettingsResponse(
            smtp.Enabled,
            smtp.Source,
            smtp.Host,
            smtp.Port,
            smtp.Username,
            SecretMasking.IsConfigured(smtp.Username),
            SecretMasking.IsConfigured(envPassword),
            SecretMasking.Mask(envPassword),
            smtp.From,
            smtp.UseStartTls,
            SecretsWritable: false),
        new WebhooksSensitiveSettingsResponse(
            webhookStatus,
            webhookEnabled,
            webhookUrlConfigured ? webhookUrl.Trim() : string.Empty,
            webhookUrlConfigured,
            webhookSecretConfigured,
            SecretMasking.Mask(webhookSecret),
            SecretsWritable: true,
            WebhooksSettingsStatus.MessageFor(webhookStatus)));
}

static async Task<UserProfile> EnsureProfileAsync(ClaimsPrincipal principal, VibeChatDbContext db, IClock clock, CancellationToken ct)
{
    var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub") ?? throw new UnauthorizedAccessException("Missing subject.");
    var profile = await db.UserProfiles.FirstOrDefaultAsync(x => x.Subject == subject, ct);
    if (profile is not null)
    {
        profile.Email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email") ?? profile.Email;
        profile.DisplayName = principal.FindFirstValue("name") ?? profile.DisplayName;
        profile.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return profile;
    }

    var email = (principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email") ?? string.Empty).Trim();
    var displayName = principal.FindFirstValue("name") ?? subject;

    // B-068: claim pending invite stub created by admin (subject pending:{email}).
    if (!string.IsNullOrWhiteSpace(email))
    {
        var normalizedEmail = email.ToLowerInvariant();
        var pendingSubject = WorkspaceRolePolicies.PendingSubjectForEmail(normalizedEmail);
        var pending = await db.UserProfiles.FirstOrDefaultAsync(
            x => x.Subject == pendingSubject || (x.Email.ToLower() == normalizedEmail && x.Subject.StartsWith("pending:")),
            ct);
        if (pending is not null)
        {
            pending.Subject = subject;
            pending.Email = email;
            pending.DisplayName = string.IsNullOrWhiteSpace(displayName) ? pending.DisplayName : displayName;
            pending.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return pending;
        }
    }

    var userId = Guid.TryParse(principal.FindFirstValue("vibechat_user_id"), out var claimId) ? new UserId(claimId) : UserId.New();
    profile = new UserProfile
    {
        Id = userId,
        Subject = subject,
        Email = string.IsNullOrWhiteSpace(email) ? $"{subject}@unknown.local" : email,
        DisplayName = displayName,
        CreatedAt = clock.UtcNow,
        UpdatedAt = clock.UtcNow
    };
    db.UserProfiles.Add(profile);
    await db.SaveChangesAsync(ct);
    return profile;
}

static async Task<Workspace?> ResolveWorkspaceAsync(WorkspaceId workspaceId, UserId userId, VibeChatDbContext db, ITenantContext tenant, CancellationToken ct)
{
    var workspace = await db.Workspaces.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == workspaceId, ct);
    if (workspace is null)
    {
        return null;
    }

    var isMember = await db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(x => x.TenantId == workspace.TenantId && x.WorkspaceId == workspace.Id && x.UserId == userId, ct);
    if (!isMember)
    {
        return null;
    }

    tenant.SetTenant(workspace.TenantId);
    return workspace;
}

static async Task<Channel?> ResolveChannelAsync(ChannelId channelId, UserId userId, VibeChatDbContext db, ITenantContext tenant, CancellationToken ct)
{
    var channel = await db.Channels.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == channelId, ct);
    if (channel is null)
    {
        return null;
    }

    var isWorkspaceMember = await db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(x => x.TenantId == channel.TenantId && x.WorkspaceId == channel.WorkspaceId && x.UserId == userId, ct);
    if (!isWorkspaceMember)
    {
        return null;
    }

    if (channel.Type is ChannelType.Private or ChannelType.Direct or ChannelType.Group)
    {
        var isChannelMember = await db.ChannelMembers.IgnoreQueryFilters().AnyAsync(x => x.TenantId == channel.TenantId && x.ChannelId == channel.Id && x.UserId == userId, ct);
        if (!isChannelMember)
        {
            return null;
        }
    }

    tenant.SetTenant(channel.TenantId);
    return channel;
}

static string BuildDirectChannelName(UserId left, UserId right)
{
    var a = left.Value;
    var b = right.Value;
    return a.CompareTo(b) <= 0 ? $"dm:{a:D}:{b:D}" : $"dm:{b:D}:{a:D}";
}

static async Task<Channel?> FindDirectChannelAsync(WorkspaceId workspaceId, UserId left, UserId right, VibeChatDbContext db, CancellationToken ct)
{
    var name = BuildDirectChannelName(left, right);
    return await db.Channels.FirstOrDefaultAsync(
        x => x.WorkspaceId == workspaceId && x.Type == ChannelType.Direct && x.Name == name,
        ct);
}

static async Task<Dictionary<ChannelId, DirectPeerInfo>> ResolveDirectPeersAsync(
    IReadOnlyCollection<Channel> channels,
    UserId currentUserId,
    VibeChatDbContext db,
    CancellationToken ct)
{
    var directIds = channels.Where(x => x.Type == ChannelType.Direct).Select(x => x.Id).ToArray();
    if (directIds.Length == 0)
    {
        return new Dictionary<ChannelId, DirectPeerInfo>();
    }

    var memberships = await db.ChannelMembers
        .Where(x => directIds.Contains(x.ChannelId) && x.UserId != currentUserId)
        .Select(x => new { x.ChannelId, x.UserId })
        .ToListAsync(ct);

    var peerIds = memberships.Select(x => x.UserId).Distinct().ToArray();
    var profiles = await db.UserProfiles
        .Where(x => peerIds.Contains(x.Id))
        .Select(x => new { x.Id, x.DisplayName })
        .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

    var result = new Dictionary<ChannelId, DirectPeerInfo>();
    foreach (var membership in memberships)
    {
        if (!profiles.TryGetValue(membership.UserId, out var displayName))
        {
            continue;
        }

        result[membership.ChannelId] = new DirectPeerInfo(membership.UserId, displayName);
    }

    return result;
}

static async Task<Message?> FindMessageInChannelAsync(
    VibeChatDbContext db,
    ChannelId channelId,
    MessageId messageId,
    CancellationToken ct)
{
    var message = await db.Messages.FirstOrDefaultAsync(x => x.Id == messageId, ct);
    if (message is null)
    {
        return null;
    }

    if (message.ConversationId == channelId)
    {
        return message;
    }

    if (message.ThreadId is not Guid threadId)
    {
        return null;
    }

    var belongs = await db.MessageThreads.AnyAsync(
        x => x.Id == threadId && x.ChannelId == channelId,
        ct);
    return belongs ? message : null;
}

static async Task<Dictionary<Guid, AttachmentResponse[]>> LoadAttachmentsByMessageAsync(
    VibeChatDbContext db,
    ChannelId channelId,
    IReadOnlyCollection<MessageId> messageIds,
    CancellationToken ct)
{
    if (messageIds.Count == 0)
    {
        return new Dictionary<Guid, AttachmentResponse[]>();
    }

    var wanted = messageIds.Select(x => x.Value).ToHashSet();
    var rows = await db.Attachments.AsNoTracking()
        .Where(x => x.ChannelId == channelId && x.MessageId != null && x.Status == AttachmentStatus.Ready)
        .OrderBy(x => x.CreatedAt)
        .ToListAsync(ct);

    return rows
        .Where(x => x.MessageId is not null && wanted.Contains(x.MessageId.Value.Value))
        .GroupBy(x => x.MessageId!.Value.Value)
        .ToDictionary(
            g => g.Key,
            g => g.Select(x => new AttachmentResponse(x.Id, x.FileName, x.ContentType, x.SizeBytes, x.Status.ToString())).ToArray());
}

static async Task<Dictionary<Guid, ReactionSummaryResponse[]>> LoadReactionSummariesByMessageAsync(
    VibeChatDbContext db,
    IReadOnlyCollection<MessageId> messageIds,
    UserId currentUserId,
    CancellationToken ct)
{
    if (messageIds.Count == 0)
    {
        return new Dictionary<Guid, ReactionSummaryResponse[]>();
    }

    var wanted = messageIds.ToHashSet();
    var rows = await db.Reactions.AsNoTracking()
        .Where(x => wanted.Contains(x.MessageId))
        .ToListAsync(ct);

    return rows
        .GroupBy(x => x.MessageId.Value)
        .ToDictionary(
            g => g.Key,
            g => g.GroupBy(x => x.Emoji)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(emojiGroup => new ReactionSummaryResponse(
                    emojiGroup.Key,
                    emojiGroup.Count(),
                    emojiGroup.Any(x => x.UserId == currentUserId)))
                .ToArray());
}

static async Task<ReactionSnapshot[]> BuildReactionSnapshotAsync(
    VibeChatDbContext db,
    MessageId messageId,
    UserId currentUserId,
    string toggledEmoji,
    bool added,
    CancellationToken ct)
{
    var rows = await db.Reactions.AsNoTracking()
        .Where(x => x.MessageId == messageId)
        .ToListAsync(ct);

    // Reflect in-memory toggle before SaveChanges for the response/outbox payload.
    if (added)
    {
        rows.Add(new Reaction
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            UserId = currentUserId,
            Emoji = toggledEmoji
        });
    }
    else
    {
        rows = rows
            .Where(x => !(x.Emoji == toggledEmoji && x.UserId == currentUserId))
            .ToList();
    }

    return rows
        .GroupBy(x => x.Emoji)
        .OrderBy(x => x.Key, StringComparer.Ordinal)
        .Select(g => new ReactionSnapshot(
            g.Key,
            g.Count(),
            g.Select(x => x.UserId.Value).Distinct().ToArray()))
        .ToArray();
}

internal sealed record ReactionSnapshot(string Emoji, int Count, Guid[] UserIds);

internal sealed record DirectPeerInfo(UserId UserId, string DisplayName);

public sealed class DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var name = "demo";
        if (Request.Headers.TryGetValue("X-Dev-User", out var headerValues) && !string.IsNullOrWhiteSpace(headerValues))
        {
            name = headerValues.ToString();
        }
        else if (Request.Query.TryGetValue("devUser", out var queryValues) && !string.IsNullOrWhiteSpace(queryValues))
        {
            name = queryValues.ToString();
        }

        var key = name.ToLowerInvariant();
        List<Claim> claims;
        if (key is "alice" or "bob" or "demo")
        {
            var (id, email, display) = key switch
            {
                "alice" => (SeedData.AliceUserId, "alice@vibechat.local", "Alice"),
                "bob" => (SeedData.BobUserId, "bob@vibechat.local", "Bob"),
                _ => (SeedData.DemoUserId, "demo@vibechat.local", "Demo")
            };

            claims =
            [
                new Claim(ClaimTypes.NameIdentifier, $"dev:{key}"),
                new Claim("sub", $"dev:{key}"),
                new Claim("vibechat_user_id", id.Value.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim("name", display),
                new Claim(ClaimTypes.Role, key == "demo" ? Role.WorkspaceOwner.ToString() : Role.Member.ToString())
            ];
        }
        else if (Request.Headers.TryGetValue("X-Dev-Email", out var emailValues)
                 && !string.IsNullOrWhiteSpace(emailValues))
        {
            // B-068 DX: dynamic invitee for pending:{email} claim tests (Development only).
            var email = emailValues.ToString().Trim().ToLowerInvariant();
            var display = Request.Headers.TryGetValue("X-Dev-Name", out var nameValues) && !string.IsNullOrWhiteSpace(nameValues)
                ? nameValues.ToString().Trim()
                : key;
            claims =
            [
                new Claim(ClaimTypes.NameIdentifier, $"dev:{key}"),
                new Claim("sub", $"dev:{key}"),
                new Claim(ClaimTypes.Email, email),
                new Claim("name", display),
                new Claim(ClaimTypes.Role, Role.Member.ToString())
            ];
        }
        else
        {
            // Unknown X-Dev-User without X-Dev-Email keeps demo fallback (prior behavior).
            claims =
            [
                new Claim(ClaimTypes.NameIdentifier, "dev:demo"),
                new Claim("sub", "dev:demo"),
                new Claim("vibechat_user_id", SeedData.DemoUserId.Value.ToString()),
                new Claim(ClaimTypes.Email, "demo@vibechat.local"),
                new Claim("name", "Demo"),
                new Claim(ClaimTypes.Role, Role.WorkspaceOwner.ToString())
            ];
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public sealed record MeResponse(Guid UserId, string Subject, string Email, string DisplayName, string[] Roles);
public sealed record WorkspaceResponse(Guid Id, string Name, string Slug, string Role);
public sealed record SpaceResponse(Guid Id, Guid WorkspaceId, string Name, int Order);
public sealed record ChannelResponse(Guid Id, Guid WorkspaceId, string Name, string Type, Guid? PeerUserId = null, string? PeerDisplayName = null, Guid? SpaceId = null);
public sealed record WorkspaceMemberResponse(Guid UserId, string DisplayName, string Email, string Role);
public sealed record WorkspaceRolesResponse(string[] AssignableRoles);
public sealed record UpdateMemberRoleRequest(string Role);
public sealed record InviteMemberRequest(string Email, string? DisplayName = null, string? Role = null);
public sealed record PresenceResponse(Guid UserId, string Status);
public sealed record OpenDirectMessageRequest(Guid UserId);
public sealed record CreateSpaceRequest(string Name, int? Order = null);
public sealed record CreateChannelRequest(string Name, string Type, Guid? SpaceId = null);
public sealed record SendMessageRequest(Guid MessageId, string IdempotencyKey, string Body, Guid? ReplyToMessageId, Guid? ThreadId, Guid[]? AttachmentIds = null);
public sealed record EditMessageRequest(string Body);
public sealed record CreateAttachmentUploadRequest(string FileName, string ContentType, long SizeBytes);
public sealed record AttachmentResponse(Guid Id, string FileName, string ContentType, long SizeBytes, string Status);
public sealed record AttachmentUploadResponse(
    Guid AttachmentId,
    string UploadUrl,
    DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    long MaxSizeBytes,
    string FileName,
    string ContentType);
public sealed record AttachmentDownloadResponse(
    Guid AttachmentId,
    string DownloadUrl,
    DateTimeOffset ExpiresAt,
    string FileName,
    string ContentType,
    long SizeBytes);
public sealed record ReactionSummaryResponse(string Emoji, int Count, bool Me);
public sealed record ToggleReactionRequest(string Emoji);
public sealed record ToggleReactionResponse(
    Guid MessageId,
    Guid ChannelId,
    string Emoji,
    bool Added,
    ReactionSummaryResponse[] Reactions);
public sealed record MessageResponse(
    Guid Id,
    Guid ChannelId,
    long Sequence,
    Guid AuthorId,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    string AuthorName = "",
    AttachmentResponse[]? Attachments = null,
    Guid? ThreadId = null,
    Guid? ReplyToMessageId = null,
    int ReplyCount = 0,
    Guid? ConversationId = null,
    ReactionSummaryResponse[]? Reactions = null);
public sealed record ThreadResponse(
    Guid Id,
    Guid ChannelId,
    Guid ParentMessageId,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    int ReplyCount,
    MessageResponse? ParentMessage = null);
public sealed record UpsertReadCursorRequest(long LastReadSequence);
public sealed record ReadCursorResponse(Guid ChannelId, Guid UserId, long LastReadSequence, DateTimeOffset UpdatedAt);
public sealed record SearchMessageHitResponse(
    Guid MessageId,
    Guid ChannelId,
    string ChannelName,
    string ChannelType,
    long Sequence,
    Guid AuthorUserId,
    string AuthorDisplayName,
    string BodyPreview,
    DateTimeOffset CreatedAt,
    double Rank);
public sealed record SearchMessagesResponse(string Query, int Limit, SearchMessageHitResponse[] Items);
public sealed record AiSummaryResponse(string Summary);
public sealed record AiSuggestReplyResponse(string Suggestion);
public sealed record AiSummaryErrorResponse(string Error, string Message);
public sealed record AuditEventResponse(
    Guid Id,
    string Action,
    string EntityType,
    string? EntityId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string MetadataJson);
public sealed record AuditEventsResponse(AuditEventResponse[] Items);
public sealed record AdminConversationResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Type,
    Guid? SpaceId,
    Guid? PeerUserId,
    string? PeerDisplayName);
public sealed record AdminConversationsResponse(AdminConversationResponse[] Items);
public sealed record AdminConversationMessageResponse(
    Guid Id,
    Guid ChannelId,
    Guid ConversationId,
    long Sequence,
    Guid AuthorId,
    string AuthorName,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    Guid? DeletedBy,
    string? DeletedByName,
    Guid? ThreadId,
    Guid? ReplyToMessageId,
    int ReplyCount,
    AttachmentResponse[] Attachments);
public sealed record AdminConversationMessagesResponse(AdminConversationMessageResponse[] Items);
public sealed record AdminHealthResponse(string Postgres, string Redis, string Storage);
public sealed record AdminDashboardResponse(
    int Users,
    int OnlineUsers,
    int Workspaces,
    int Channels,
    int Messages,
    int RealtimeConnections,
    int OutboxPending,
    int ProcessingFailures,
    AdminHealthResponse Health,
    string AppVersion,
    string GrafanaUrl);
public sealed record AiSensitiveSettingsResponse(
    bool ProcessEnabled,
    string ProcessSource,
    bool WorkspaceEnabled,
    string Provider,
    bool ApiKeyConfigured,
    string? ApiKeyMask,
    bool SecretsWritable);
public sealed record EmailSensitiveSettingsResponse(
    bool Enabled,
    string Source,
    string SmtpHost,
    int SmtpPort,
    string SmtpUsername,
    bool SmtpUsernameConfigured,
    bool SmtpPasswordConfigured,
    string? SmtpPasswordMask,
    string SmtpFrom,
    bool UseStartTls,
    bool SecretsWritable);
public sealed record WebhooksSensitiveSettingsResponse(
    string Status,
    bool Enabled,
    string Url,
    bool UrlConfigured,
    bool SecretConfigured,
    string? SecretMask,
    bool SecretsWritable,
    string Message);
public sealed record SensitiveSettingsResponse(
    Guid WorkspaceId,
    AiSensitiveSettingsResponse Ai,
    EmailSensitiveSettingsResponse Email,
    WebhooksSensitiveSettingsResponse Webhooks);
public sealed record UpdateAiSensitiveSettingsRequest(
    bool? WorkspaceEnabled = null,
    string? Provider = null,
    string? ApiKey = null);
public sealed record UpdateEmailSensitiveSettingsRequest(
    bool? Enabled = null,
    string? SmtpHost = null,
    int? SmtpPort = null,
    string? SmtpUsername = null,
    string? SmtpPassword = null,
    string? SmtpFrom = null,
    bool? UseStartTls = null);
public sealed record UpdateWebhooksSensitiveSettingsRequest(
    bool? Enabled = null,
    string? Url = null,
    string? Secret = null);
public sealed record UpdateSensitiveSettingsRequest(
    Guid? WorkspaceId = null,
    UpdateAiSensitiveSettingsRequest? Ai = null,
    UpdateEmailSensitiveSettingsRequest? Email = null,
    UpdateWebhooksSensitiveSettingsRequest? Webhooks = null);

public partial class Program;
