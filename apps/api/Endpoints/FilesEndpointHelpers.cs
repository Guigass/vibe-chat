using VibeChat.Api;
using VibeChat.Files;

namespace VibeChat.Api.Endpoints;

internal static class FilesEndpointHelpers
{
    internal static AttachmentResponse ToAttachmentResponse(Attachment attachment) =>
        new(
            attachment.Id,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.Status.ToString(),
            attachment.Kind.ToString(),
            attachment.DurationMs,
            attachment.Waveform,
            attachment.ThumbnailStatus?.ToString(),
            attachment.Width,
            attachment.Height,
            attachment.PageCount);
}
