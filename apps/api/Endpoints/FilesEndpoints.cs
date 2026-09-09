using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Infrastructure;
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

            if (attachment.Status == AttachmentStatus.Ready)
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
            attachment.Status = AttachmentStatus.Ready;
            attachment.ReadyAt = clock.UtcNow;
            attachment.ChecksumSha256 = string.IsNullOrWhiteSpace(stat.ETag) ? null : stat.ETag.Trim('"');
            if (AttachmentPolicies.IsThumbnailEligible(attachment.ContentType))
            {
                attachment.ThumbnailStatus = ThumbnailStatus.Pending;
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
            if (attachment is null || attachment.Status != AttachmentStatus.Ready)
            {
                return Results.NotFound();
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
    }
}
