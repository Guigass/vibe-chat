using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Compliance;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using static VibeChat.Api.Endpoints.FilesEndpointHelpers;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class FilesEndpoints
{
    internal static void MapFiles(this RouteGroupBuilder v1)
    {
        v1.MapPost("/channels/{channelId:guid}/attachments", async (
            Guid channelId,
            CreateAttachmentUploadRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IObjectStorage storage,
            FilesSettingsResolver filesSettings,
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

            var files = await filesSettings.ResolveAsync(channel.TenantId, ct);
            var maxSize = files.MaxSizeBytes;
            var allowed = files.AllowedContentTypes.ToArray();
            var uploadTtl = TimeSpan.FromSeconds(files.PresignUploadTtlSeconds);
            var kind = Enum.TryParse<AttachmentKind>(request.Kind, ignoreCase: true, out var parsedKind)
                ? parsedKind
                : AttachmentKind.File;

            if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.ContentType) || request.SizeBytes <= 0)
            {
                return Results.BadRequest(new { error = "fileName, contentType and sizeBytes are required." });
            }

            int[]? waveform = null;
            if (kind == AttachmentKind.Audio)
            {
                maxSize = files.AudioMaxSizeBytes;
                var maxDurationMs = files.AudioMaxDurationMs;
                if (!AttachmentPolicies.IsAllowedAudioContentType(request.ContentType))
                {
                    return Results.BadRequest(new { error = "Audio content type is not allowed." });
                }

                if (!request.DurationMs.HasValue || request.DurationMs <= 0)
                {
                    return Results.BadRequest(new { error = "durationMs is required for audio attachments." });
                }

                if (request.DurationMs > maxDurationMs)
                {
                    return Results.BadRequest(new { error = $"Audio exceeds max duration of {maxDurationMs} ms." });
                }

                if (!AttachmentPolicies.IsValidWaveform(request.Waveform))
                {
                    return Results.BadRequest(new { error = "Waveform must contain up to 100 values between 0 and 100." });
                }

                waveform = AttachmentPolicies.NormalizeWaveform(request.Waveform);
            }
            else if (kind == AttachmentKind.Video)
            {
                maxSize = files.VideoMaxSizeBytes;
                var maxDurationMs = files.VideoMaxDurationMs;
                if (!AttachmentPolicies.IsAllowedVideoContentType(request.ContentType))
                {
                    return Results.BadRequest(new { error = "Video content type is not allowed." });
                }

                if (!request.DurationMs.HasValue || request.DurationMs <= 0)
                {
                    return Results.BadRequest(new { error = "durationMs is required for video attachments." });
                }

                if (request.DurationMs > maxDurationMs)
                {
                    return Results.BadRequest(new { error = $"Video exceeds max duration of {maxDurationMs} ms." });
                }
            }
            else
            {
                if (!AttachmentPolicies.IsAllowedContentType(request.ContentType, allowed))
                {
                    return Results.BadRequest(new { error = "Content type is not allowed." });
                }
            }

            if (request.SizeBytes > maxSize)
            {
                return Results.BadRequest(new { error = $"File exceeds max size of {maxSize} bytes." });
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
                ReferenceCount = 1,
                Kind = kind,
                DurationMs = kind is AttachmentKind.Audio or AttachmentKind.Video ? request.DurationMs : null,
                Waveform = kind == AttachmentKind.Audio ? waveform : null,
                Width = kind == AttachmentKind.Video ? request.Width : null,
                Height = kind == AttachmentKind.Video ? request.Height : null,
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
        }).RequirePermission(Permissions.Files.Upload, Permissions.Message.Send);

        v1.MapPost("/channels/{channelId:guid}/attachments/{attachmentId:guid}/complete", async (
            Guid channelId,
            Guid attachmentId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IObjectStorage storage,
            FilesSettingsResolver filesSettings,
            IOutboxWriter outbox,
            IOptions<MalwareScanOptions> scanOptions,
            IMalwareScanner scanner,
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

            var attachment = await db.Attachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.ChannelId == channel.Id, ct);
            if (attachment is null)
            {
                return Results.NotFound();
            }

            if (attachment.UploadedBy != profile.Id)
            {
                return Results.Forbid();
            }

            if (attachment.Status == AttachmentStatus.Ready
                || attachment.ScanStatus is AttachmentScanStatus.PendingScan
                    or AttachmentScanStatus.Quarantined
                    or AttachmentScanStatus.ScanFailed)
            {
                return Results.Ok(ToAttachmentResponse(attachment));
            }

            var stat = await storage.StatObjectAsync(attachment.StorageKey, ct);
            if (stat is null || stat.SizeBytes <= 0)
            {
                attachment.Status = AttachmentStatus.Failed;
                await db.SaveChangesAsync(ct);
                return Results.BadRequest(new { error = "Uploaded object was not found in storage." });
            }

            var files = await filesSettings.ResolveAsync(channel.TenantId, ct);
            var maxSize = attachment.Kind switch
            {
                AttachmentKind.Audio => files.AudioMaxSizeBytes,
                AttachmentKind.Video => files.VideoMaxSizeBytes,
                _ => files.MaxSizeBytes
            };
            if (stat.SizeBytes > maxSize || stat.SizeBytes > attachment.SizeBytes)
            {
                attachment.Status = AttachmentStatus.Failed;
                await db.SaveChangesAsync(ct);
                return Results.BadRequest(new { error = "Uploaded object exceeds declared or allowed size." });
            }

            attachment.SizeBytes = stat.SizeBytes;
            attachment.ChecksumSha256 = string.IsNullOrWhiteSpace(stat.ETag) ? null : stat.ETag.Trim('"');
            var malware = scanOptions.Value;
            if (!malware.Enabled)
            {
                if (scanner is not NullMalwareScanner)
                {
                    attachment.Status = AttachmentStatus.Failed;
                    attachment.ScanStatus = AttachmentScanStatus.ScanFailed;
                    attachment.ScanReason = "ScannerMissing";
                    await db.SaveChangesAsync(ct);
                    return Results.Json(new { error = "ScannerMissing" }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                attachment.ScanStatus = AttachmentScanStatus.NotRequired;
                attachment.ScanEngine = NullMalwareScanner.EngineName;
                attachment.ScanSignatureVersion = scanner.SignatureVersion;
                attachment.Status = AttachmentStatus.Ready;
                attachment.ReadyAt = clock.UtcNow;
                if (AttachmentPolicies.IsThumbnailEligible(attachment.ContentType))
                {
                    attachment.ThumbnailStatus = ThumbnailStatus.Pending;
                }
            }
            else if (scanner is NullMalwareScanner)
            {
                attachment.Status = AttachmentStatus.PendingUpload;
                attachment.ScanStatus = AttachmentScanStatus.ScanFailed;
                attachment.ScanReason = "ScannerMissing";
                attachment.ScannedAt = clock.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Json(new { error = "ScannerMissing" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            else
            {
                attachment.Status = AttachmentStatus.PendingUpload;
                attachment.ScanStatus = AttachmentScanStatus.PendingScan;
                attachment.ScanEngine = null;
                attachment.ScanReason = null;
                attachment.ScanRequestedAt = clock.UtcNow;
                attachment.ReadyAt = null;
                VibeChatMetrics.AttachmentScanQueue.Add(1);
                outbox.Add(new OutboxMessage
                {
                    TenantId = channel.TenantId,
                    Type = AttachmentScanRunner.RequestedEvent,
                    Payload = JsonSerializer.Serialize(new
                    {
                        tenantId = channel.TenantId.Value,
                        channelId,
                        attachmentId = attachment.Id
                    })
                });
                audit.Add(new AuditEvent
                {
                    TenantId = channel.TenantId,
                    ActorUserId = profile.Id,
                    Action = AuditActions.AttachmentScan,
                    EntityType = "Attachment",
                    EntityId = attachment.Id.ToString(),
                    MetadataJson = JsonSerializer.Serialize(new { stage = "requested" })
                });
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToAttachmentResponse(attachment));
            }

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
                    readyAt = attachment.ReadyAt,
                    thumbnailStatus = attachment.ThumbnailStatus?.ToString()
                })
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToAttachmentResponse(attachment));
        }).RequirePermission(Permissions.Files.Upload);

        v1.MapGet("/channels/{channelId:guid}/attachments/{attachmentId:guid}/download", async (
            Guid channelId,
            Guid attachmentId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IObjectStorage storage,
            FilesSettingsResolver filesSettings,
            IOptions<MalwareScanOptions> scanOptions,
            DlpService dlp,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var attachment = await db.Attachments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == attachmentId && x.ChannelId == channel.Id, ct);
            if (attachment is null || !AttachmentScanPolicies.CanDownload(attachment, scanOptions.Value.FailClosed))
            {
                return Results.NotFound();
            }

            var denied = await DlpGuard.BlockAsync(dlp, channel.TenantId, channel.WorkspaceId, channel.SpaceId, channel.Id.Value, channel.Type.ToString(), DlpActions.Download, null, attachment.Id, ct);
            if (denied is not null)
            {
                return denied;
            }

            var files = await filesSettings.ResolveAsync(channel.TenantId, ct);
            var downloadTtl = TimeSpan.FromSeconds(files.PresignDownloadTtlSeconds);
            var download = await storage.CreateDownloadUrlAsync(attachment.StorageKey, attachment.FileName, downloadTtl, ct);
            return Results.Ok(new AttachmentDownloadResponse(
                attachment.Id,
                download.Url.ToString(),
                download.ExpiresAt,
                attachment.FileName,
                attachment.ContentType,
                attachment.SizeBytes));
        }).RequirePermission(Permissions.Files.Download, Permissions.Message.Read);

        v1.MapGet("/channels/{channelId:guid}/attachments/{attachmentId:guid}/thumbnail", async (
            Guid channelId,
            Guid attachmentId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IObjectStorage storage,
            FilesSettingsResolver filesSettings,
            DlpService dlp,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }


            var attachment = await db.Attachments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == attachmentId && x.ChannelId == channel.Id, ct);
            if (attachment is null
                || attachment.Status != AttachmentStatus.Ready
                || attachment.ThumbnailStatus != ThumbnailStatus.Ready
                || string.IsNullOrWhiteSpace(attachment.ThumbnailKey))
            {
                return Results.NotFound();
            }

            var denied = await DlpGuard.BlockAsync(dlp, channel.TenantId, channel.WorkspaceId, channel.SpaceId, channel.Id.Value, channel.Type.ToString(), DlpActions.Download, null, attachment.Id, ct);
            if (denied is not null)
            {
                return denied;
            }

            var files = await filesSettings.ResolveAsync(channel.TenantId, ct);
            var downloadTtl = TimeSpan.FromSeconds(files.PresignDownloadTtlSeconds);
            var download = await storage.CreateDownloadUrlAsync(
                attachment.ThumbnailKey,
                AttachmentPolicies.ThumbnailFileName,
                downloadTtl,
                ct);
            return Results.Ok(new AttachmentThumbnailResponse(
                attachment.Id,
                download.Url.ToString(),
                download.ExpiresAt,
                AttachmentPolicies.ThumbnailContentType,
                attachment.Width,
                attachment.Height,
                attachment.PageCount));
        }).RequirePermission(Permissions.Files.Download, Permissions.Message.Read);

        v1.MapPost("/channels/{channelId:guid}/attachments/{attachmentId:guid}/rescan", async (
            Guid channelId,
            Guid attachmentId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IOptions<MalwareScanOptions> scanOptions,
            IOutboxWriter outbox,
            IPermissionChecker permissions,
            IClock clock,
            CancellationToken ct) =>
        {
            if (!scanOptions.Value.Enabled)
            {
                return Results.NotFound(new { error = "MalwareScanDisabled" });
            }

            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var attachment = await db.Attachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.ChannelId == channel.Id, ct);
            if (attachment is null)
            {
                return Results.NotFound();
            }

            var canManage = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Files.QuarantineManage, ct);
            if (attachment.UploadedBy != profile.Id && !canManage)
            {
                return Results.Forbid();
            }

            if (attachment.ScanStatus == AttachmentScanStatus.PendingScan)
            {
                return Results.Ok(ToAttachmentResponse(attachment));
            }

            if (attachment.ScanStatus != AttachmentScanStatus.ScanFailed)
            {
                return Results.Conflict(new { error = "ScanNotRetryable" });
            }

            attachment.ScanStatus = AttachmentScanStatus.PendingScan;
            attachment.ScanReason = null;
            attachment.ScanRequestedAt = clock.UtcNow;
            attachment.Status = AttachmentStatus.PendingUpload;
            attachment.ReadyAt = null;
            VibeChatMetrics.AttachmentScanQueue.Add(1);
            outbox.Add(new OutboxMessage
            {
                TenantId = channel.TenantId,
                Type = AttachmentScanRunner.RequestedEvent,
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = channel.TenantId.Value,
                    channelId,
                    attachmentId = attachment.Id
                })
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToAttachmentResponse(attachment));
        }).RequirePermission(Permissions.Files.Upload);

        v1.MapGet("/workspaces/{workspaceId:guid}/quarantine", async (
            Guid workspaceId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IOptions<MalwareScanOptions> scanOptions,
            IClock clock,
            CancellationToken ct) =>
        {
            if (!scanOptions.Value.Enabled)
            {
                return Results.NotFound(new { error = "MalwareScanDisabled" });
            }

            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var rows = await db.Attachments.AsNoTracking()
                .Where(x => x.TenantId == workspace.TenantId
                    && x.ScanStatus == AttachmentScanStatus.Quarantined
                    && x.Status != AttachmentStatus.Failed)
                .OrderByDescending(x => x.ScannedAt)
                .Take(100)
                .ToListAsync(ct);
            var items = rows.Select(x => new QuarantineItemResponse(
                x.Id,
                x.ChannelId.Value,
                x.FileName,
                x.ContentType,
                x.SizeBytes,
                x.ScanStatus?.ToString() ?? string.Empty,
                x.ScanReason,
                x.ScannedAt)).ToList();
            return Results.Ok(new { items });
        }).RequirePermission(Permissions.Files.QuarantineManage);

        v1.MapPost("/workspaces/{workspaceId:guid}/quarantine/{attachmentId:guid}/release", async (
            Guid workspaceId,
            Guid attachmentId,
            QuarantineDecisionRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IOptions<MalwareScanOptions> scanOptions,
            IOutboxWriter outbox,
            IAuditWriter audit,
            IClock clock,
            CancellationToken ct) =>
        {
            if (!scanOptions.Value.Enabled)
            {
                return Results.NotFound(new { error = "MalwareScanDisabled" });
            }

            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var justification = (request.Justification ?? string.Empty).Trim();
            if (justification.Length is < 3 or > 500)
            {
                return Results.BadRequest(new { error = "JustificationRequired" });
            }

            var attachment = await db.Attachments.FirstOrDefaultAsync(
                x => x.Id == attachmentId && x.TenantId == workspace.TenantId && x.ScanStatus == AttachmentScanStatus.Quarantined,
                ct);
            if (attachment is null || attachment.Status == AttachmentStatus.Failed)
            {
                return Results.NotFound();
            }

            var now = clock.UtcNow;
            attachment.ScanStatus = AttachmentScanStatus.Clean;
            attachment.ScanReason = "Released";
            attachment.Status = AttachmentStatus.Ready;
            attachment.ReadyAt = now;
            if (AttachmentPolicies.IsThumbnailEligible(attachment.ContentType) && attachment.ThumbnailStatus is null)
            {
                attachment.ThumbnailStatus = ThumbnailStatus.Pending;
            }

            outbox.Add(new OutboxMessage
            {
                TenantId = workspace.TenantId,
                Type = "files.attachment.ready",
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = workspace.TenantId.Value,
                    channelId = attachment.ChannelId.Value,
                    attachmentId = attachment.Id,
                    fileName = attachment.FileName,
                    contentType = attachment.ContentType,
                    sizeBytes = attachment.SizeBytes,
                    readyAt = attachment.ReadyAt,
                    thumbnailStatus = attachment.ThumbnailStatus?.ToString()
                })
            });
            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.AttachmentQuarantineRelease,
                EntityType = "Attachment",
                EntityId = attachment.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { justification })
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToAttachmentResponse(attachment));
        }).RequirePermission(Permissions.Files.QuarantineManage);

        v1.MapPost("/workspaces/{workspaceId:guid}/quarantine/{attachmentId:guid}/delete", async (
            Guid workspaceId,
            Guid attachmentId,
            QuarantineDecisionRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IObjectStorage storage,
            IOptions<MalwareScanOptions> scanOptions,
            IAuditWriter audit,
            IClock clock,
            CancellationToken ct) =>
        {
            if (!scanOptions.Value.Enabled)
            {
                return Results.NotFound(new { error = "MalwareScanDisabled" });
            }

            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var justification = (request.Justification ?? string.Empty).Trim();
            if (justification.Length is < 3 or > 500)
            {
                return Results.BadRequest(new { error = "JustificationRequired" });
            }

            var attachment = await db.Attachments.FirstOrDefaultAsync(
                x => x.Id == attachmentId && x.TenantId == workspace.TenantId
                    && (x.ScanStatus == AttachmentScanStatus.Quarantined || x.ScanStatus == AttachmentScanStatus.ScanFailed),
                ct);
            if (attachment is null || attachment.Status == AttachmentStatus.Failed)
            {
                return Results.NotFound();
            }

            var siblings = await db.Attachments.CountAsync(x => x.StorageKey == attachment.StorageKey && x.Id != attachment.Id, ct);
            if (AttachmentReferencePolicies.CanDeleteBlob(siblings))
            {
                await storage.DeleteObjectAsync(attachment.StorageKey, ct);
            }

            attachment.Status = AttachmentStatus.Failed;
            attachment.ScanReason = "Deleted";
            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.AttachmentQuarantineDelete,
                EntityType = "Attachment",
                EntityId = attachment.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { justification })
            });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.Files.QuarantineManage);
    }
}
