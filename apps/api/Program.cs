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
using VibeChat.Files;
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
        return new ChannelResponse(x.Id.Value, x.WorkspaceId.Value, displayName, x.Type.ToString(), peer?.UserId.Value, peer?.DisplayName);
    }).ToArray();
    return Results.Ok(response);
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

    if (Enum.TryParse<ChannelType>(request.Type, true, out var parsed) && parsed is ChannelType.Direct)
    {
        return Results.BadRequest(new { error = "Use POST /workspaces/{id}/dms to open direct messages." });
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
    var rows = await (
        from m in db.Messages
        where m.ConversationId == channel.Id && m.Sequence > (after ?? 0)
        join u in db.UserProfiles on m.AuthorId equals u.Id into authors
        from u in authors.DefaultIfEmpty()
        orderby m.Sequence
        select new
        {
            Message = m,
            AuthorName = u != null ? u.DisplayName : m.AuthorId.Value.ToString()
        })
        .Take(take)
        .ToArrayAsync(ct);

    var messageIds = rows.Select(x => x.Message.Id).ToArray();
    var attachmentsByMessage = await LoadAttachmentsByMessageAsync(db, messageIds, ct);
    var messages = rows.Select(x => new MessageResponse(
        x.Message.Id.Value,
        x.Message.ConversationId.Value,
        x.Message.Sequence,
        x.Message.AuthorId.Value,
        x.Message.DeletedAt == null ? x.Message.Body : string.Empty,
        x.Message.CreatedAt,
        x.Message.EditedAt,
        x.Message.DeletedAt,
        x.AuthorName,
        x.Message.DeletedAt == null && attachmentsByMessage.TryGetValue(x.Message.Id.Value, out var atts) ? atts : [])).ToArray();
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
            new MessageResponse(result.MessageId.Value, channel.Id.Value, result.Sequence, profile.Id.Value, request.Body?.Trim() ?? string.Empty, result.CreatedAt, null, null, profile.DisplayName, attachments));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
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

    var message = await db.Messages.FirstOrDefaultAsync(x => x.ConversationId == channel.Id && x.Id == new MessageId(messageId), ct);
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
    return Results.Ok(new MessageResponse(message.Id.Value, message.ConversationId.Value, message.Sequence, message.AuthorId.Value, message.Body, message.CreatedAt, message.EditedAt, message.DeletedAt, profile.DisplayName, attachments));
});

v1.MapDelete("/channels/{channelId:guid}/messages/{messageId:guid}", async (Guid channelId, Guid messageId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IOutboxWriter outbox, IAuditWriter audit, IClock clock, CancellationToken ct) =>
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
            messageId,
            sequence = message.Sequence,
            deletedAt = message.DeletedAt
        })
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

static async Task<Dictionary<Guid, AttachmentResponse[]>> LoadAttachmentsByMessageAsync(
    VibeChatDbContext db,
    IReadOnlyCollection<MessageId> messageIds,
    CancellationToken ct)
{
    if (messageIds.Count == 0)
    {
        return new Dictionary<Guid, AttachmentResponse[]>();
    }

    var rows = await db.Attachments.AsNoTracking()
        .Where(x => x.MessageId != null && messageIds.Contains(x.MessageId.Value) && x.Status == AttachmentStatus.Ready)
        .OrderBy(x => x.CreatedAt)
        .Select(x => new
        {
            MessageId = x.MessageId!.Value.Value,
            Attachment = new AttachmentResponse(x.Id, x.FileName, x.ContentType, x.SizeBytes, x.Status.ToString())
        })
        .ToListAsync(ct);

    return rows
        .GroupBy(x => x.MessageId)
        .ToDictionary(g => g.Key, g => g.Select(x => x.Attachment).ToArray());
}

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
public sealed record ChannelResponse(Guid Id, Guid WorkspaceId, string Name, string Type, Guid? PeerUserId = null, string? PeerDisplayName = null);
public sealed record WorkspaceMemberResponse(Guid UserId, string DisplayName, string Email, string Role);
public sealed record OpenDirectMessageRequest(Guid UserId);
public sealed record CreateChannelRequest(string Name, string Type);
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
    AttachmentResponse[]? Attachments = null);
public sealed record UpsertReadCursorRequest(long LastReadSequence);
public sealed record ReadCursorResponse(Guid ChannelId, Guid UserId, long LastReadSequence, DateTimeOffset UpdatedAt);
public sealed record AiSummaryResponse(string Summary);
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

public partial class Program;
