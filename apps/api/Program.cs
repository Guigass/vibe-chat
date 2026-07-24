using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.Realtime;
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("localhost", policy => policy
        .WithOrigins("http://localhost:4200", "https://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var auth = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "smart";
    options.DefaultChallengeScheme = "smart";
});

auth.AddPolicyScheme("smart", "JWT or DevAuth", options =>
{
    options.ForwardDefaultSelector = context =>
        context.Request.Headers.ContainsKey("X-Dev-User") && builder.Environment.IsDevelopment()
            ? DevAuthHandler.SchemeName
            : JwtBearerDefaults.AuthenticationScheme;
});

auth.AddJwtBearer(options =>
{
    options.Authority = builder.Configuration["Authentication:Authority"];
    options.Audience = builder.Configuration["Authentication:Audience"];
    options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", true);
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

    var channels = await db.Channels
        .Where(x => x.WorkspaceId == workspace.Id)
        .OrderBy(x => x.Name)
        .Select(x => new ChannelResponse(x.Id.Value, x.WorkspaceId.Value, x.Name, x.Type.ToString()))
        .ToArrayAsync(ct);
    return Results.Ok(channels);
});

v1.MapPost("/workspaces/{workspaceId:guid}/channels", async (Guid workspaceId, CreateChannelRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IAuditWriter audit, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    if (workspace is null || !await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Channel.Create, ct))
    {
        return Results.Forbid();
    }

    var channel = new Channel
    {
        Id = ChannelId.New(),
        TenantId = workspace.TenantId,
        WorkspaceId = workspace.Id,
        Name = request.Name.Trim(),
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
        MetadataJson = JsonSerializer.Serialize(new { workspaceId, type = channel.Type.ToString() })
    });
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/channels/{channel.Id.Value}", new ChannelResponse(channel.Id.Value, channel.WorkspaceId.Value, channel.Name, channel.Type.ToString()));
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
    var messages = await db.Messages
        .Where(x => x.ConversationId == channel.Id && x.Sequence > (after ?? 0))
        .OrderBy(x => x.Sequence)
        .Take(take)
        .Select(x => new MessageResponse(x.Id.Value, x.ConversationId.Value, x.Sequence, x.AuthorId.Value, x.DeletedAt == null ? x.Body : string.Empty, x.CreatedAt, x.EditedAt, x.DeletedAt))
        .ToArrayAsync(ct);
    return Results.Ok(messages);
});

v1.MapPost("/channels/{channelId:guid}/messages", async (Guid channelId, SendMessageRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IMessageWriter writer, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    if (request.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey) || string.IsNullOrWhiteSpace(request.Body))
    {
        return Results.BadRequest(new { error = "messageId, idempotencyKey and body are required." });
    }

    var result = await writer.SendAsync(new SendMessageCommand(channel.TenantId, profile.Id, channel.Id, new MessageId(request.MessageId), request.IdempotencyKey, request.Body, request.ReplyToMessageId is null ? null : new MessageId(request.ReplyToMessageId.Value), request.ThreadId), ct);
    return Results.Accepted($"/api/v1/channels/{channel.Id.Value}/messages?after={result.Sequence - 1}", result);
});

v1.MapPut("/channels/{channelId:guid}/messages/{messageId:guid}", async (Guid channelId, Guid messageId, EditMessageRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IOutboxWriter outbox, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var message = await db.Messages.FirstOrDefaultAsync(x => x.ConversationId == channel.Id && x.Id == new MessageId(messageId), ct);
    if (message is null || message.AuthorId != profile.Id || message.DeletedAt is not null)
    {
        return Results.NotFound();
    }

    message.Body = request.Body;
    message.EditedAt = clock.UtcNow;
    outbox.Add(new OutboxMessage
    {
        TenantId = channel.TenantId,
        Type = nameof(MessageEditedEvent),
        Payload = JsonSerializer.Serialize(new { tenantId = channel.TenantId.Value, channelId, messageId, sequence = message.Sequence, editedAt = message.EditedAt })
    });
    await db.SaveChangesAsync(ct);
    return Results.Ok(new MessageResponse(message.Id.Value, message.ConversationId.Value, message.Sequence, message.AuthorId.Value, message.Body, message.CreatedAt, message.EditedAt, message.DeletedAt));
});

v1.MapDelete("/channels/{channelId:guid}/messages/{messageId:guid}", async (Guid channelId, Guid messageId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IOutboxWriter outbox, IAuditWriter audit, IClock clock, CancellationToken ct) =>
{
    var profile = await EnsureProfileAsync(http.User, db, clock, ct);
    var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
    if (channel is null)
    {
        return Results.Forbid();
    }

    var message = await db.Messages.FirstOrDefaultAsync(x => x.ConversationId == channel.Id && x.Id == new MessageId(messageId), ct);
    if (message is null)
    {
        return Results.NotFound();
    }

    message.DeletedAt = clock.UtcNow;
    message.DeletedBy = profile.Id;
    outbox.Add(new OutboxMessage
    {
        TenantId = channel.TenantId,
        Type = nameof(MessageDeletedEvent),
        Payload = JsonSerializer.Serialize(new { tenantId = channel.TenantId.Value, channelId, messageId, sequence = message.Sequence, deletedAt = message.DeletedAt })
    });
    audit.Add(new AuditEvent { TenantId = channel.TenantId, ActorUserId = profile.Id, Action = AuditActions.MessageDelete, EntityType = "Message", EntityId = message.Id.ToString(), MetadataJson = JsonSerializer.Serialize(new { channelId, message.Sequence }) });
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

v1.MapGet("/admin/dashboard", async (HttpContext http, VibeChatDbContext db, IDashboardQuery dashboard, IClock clock, CancellationToken ct) =>
{
    await EnsureProfileAsync(http.User, db, clock, ct);
    return Results.Ok(await dashboard.GetStatsAsync(ct));
});

v1.MapGet("/admin/health-summary", async (HealthCheckService health, CancellationToken ct) =>
{
    var report = await health.CheckHealthAsync(ct);
    return Results.Ok(new { status = report.Status.ToString(), checks = report.Entries.ToDictionary(x => x.Key, x => x.Value.Status.ToString()) });
});

v1.MapGet("/admin/version", () => Results.Ok(new { name = "VibeChat.Api", version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev" }));

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

    return Results.Ok(new AiSummaryResponse(await summarize.SummarizeAsync(workspace.TenantId, workspace.Id, channel.Id, ct)));
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

app.Run();

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

    var userId = Guid.TryParse(principal.FindFirstValue("vibechat_user_id"), out var claimId) ? new UserId(claimId) : UserId.New();
    profile = new UserProfile
    {
        Id = userId,
        Subject = subject,
        Email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email") ?? $"{subject}@unknown.local",
        DisplayName = principal.FindFirstValue("name") ?? subject,
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

public sealed class DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var name = Request.Headers.TryGetValue("X-Dev-User", out var values) ? values.ToString() : "demo";
        var (id, email, display) = name.ToLowerInvariant() switch
        {
            "alice" => (SeedData.AliceUserId, "alice@vibechat.local", "Alice"),
            "bob" => (SeedData.BobUserId, "bob@vibechat.local", "Bob"),
            _ => (SeedData.DemoUserId, "demo@vibechat.local", "Demo")
        };

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, $"dev:{name.ToLowerInvariant()}"),
            new Claim("sub", $"dev:{name.ToLowerInvariant()}"),
            new Claim("vibechat_user_id", id.Value.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim("name", display),
            new Claim(ClaimTypes.Role, name.Equals("demo", StringComparison.OrdinalIgnoreCase) ? Role.WorkspaceOwner.ToString() : Role.Member.ToString())
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public sealed record MeResponse(Guid UserId, string Subject, string Email, string DisplayName, string[] Roles);
public sealed record WorkspaceResponse(Guid Id, string Name, string Slug, string Role);
public sealed record ChannelResponse(Guid Id, Guid WorkspaceId, string Name, string Type);
public sealed record CreateChannelRequest(string Name, string Type);
public sealed record SendMessageRequest(Guid MessageId, string IdempotencyKey, string Body, Guid? ReplyToMessageId, Guid? ThreadId);
public sealed record EditMessageRequest(string Body);
public sealed record MessageResponse(Guid Id, Guid ChannelId, long Sequence, Guid AuthorId, string Body, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, DateTimeOffset? DeletedAt);
public sealed record UpsertReadCursorRequest(long LastReadSequence);
public sealed record ReadCursorResponse(Guid ChannelId, Guid UserId, long LastReadSequence, DateTimeOffset UpdatedAt);
public sealed record AiSummaryResponse(string Summary);

public partial class Program;
