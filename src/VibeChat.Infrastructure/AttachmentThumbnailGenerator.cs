using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using VibeChat.Files;

namespace VibeChat.Infrastructure;

/// <summary>Decoded thumbnail bytes ready for storage (B-090).</summary>
public sealed record AttachmentThumbnailCodecResult(
    bool Success,
    byte[]? WebpBytes,
    int Width,
    int Height,
    int? PageCount,
    string? Error);

/// <summary>CPU-bound thumbnail codec — ImageSharp for images, PDFtoImage for PDF page 1.</summary>
public static class AttachmentThumbnailCodec
{
    public static AttachmentThumbnailCodecResult FromImage(Stream input)
    {
        try
        {
            using var image = Image.Load(input);
            if (!AttachmentPolicies.IsWithinThumbnailInputLimits(image.Width, image.Height))
            {
                return new AttachmentThumbnailCodecResult(
                    false, null, image.Width, image.Height, null, "InputTooLarge");
            }

            var originalWidth = image.Width;
            var originalHeight = image.Height;
            ResizeToMaxEdge(image, AttachmentPolicies.ThumbnailMaxEdgePx);

            using var output = new MemoryStream();
            image.Save(output, new WebpEncoder { Quality = 80 });
            return new AttachmentThumbnailCodecResult(
                true, output.ToArray(), originalWidth, originalHeight, null, null);
        }
        catch (UnknownImageFormatException)
        {
            return new AttachmentThumbnailCodecResult(false, null, 0, 0, null, "UnsupportedImage");
        }
        catch (Exception ex)
        {
            return new AttachmentThumbnailCodecResult(false, null, 0, 0, null, Truncate(ex.Message));
        }
    }

    public static AttachmentThumbnailCodecResult FromPdf(Stream input)
    {
        try
        {
            using var copy = new MemoryStream();
            input.CopyTo(copy);
            var pdfBytes = copy.ToArray();
            if (pdfBytes.Length == 0)
            {
                return new AttachmentThumbnailCodecResult(false, null, 0, 0, null, "EmptyPdf");
            }

#pragma warning disable CA1416 // PDFtoImage supports linux/windows/macos — our Docker runtime targets
            var pageCount = PDFtoImage.Conversion.GetPageCount(pdfBytes);
            if (pageCount <= 0)
            {
                return new AttachmentThumbnailCodecResult(false, null, 0, 0, 0, "EmptyPdf");
            }

            using var skBitmap = PDFtoImage.Conversion.ToImage(pdfBytes, page: 0);
#pragma warning restore CA1416
            if (!AttachmentPolicies.IsWithinThumbnailInputLimits(skBitmap.Width, skBitmap.Height))
            {
                return new AttachmentThumbnailCodecResult(
                    false, null, skBitmap.Width, skBitmap.Height, pageCount, "InputTooLarge");
            }

            using var encoded = skBitmap.Encode(SKEncodedImageFormat.Png, 90);
            using var pngStream = new MemoryStream();
            encoded.AsStream().CopyTo(pngStream);
            pngStream.Position = 0;

            using var image = Image.Load(pngStream);
            var originalWidth = image.Width;
            var originalHeight = image.Height;
            ResizeToMaxEdge(image, AttachmentPolicies.ThumbnailMaxEdgePx);

            using var output = new MemoryStream();
            image.Save(output, new WebpEncoder { Quality = 80 });
            return new AttachmentThumbnailCodecResult(
                true, output.ToArray(), originalWidth, originalHeight, pageCount, null);
        }
        catch (Exception ex)
        {
            return new AttachmentThumbnailCodecResult(false, null, 0, 0, null, Truncate(ex.Message));
        }
    }

    public static AttachmentThumbnailCodecResult FromContent(Stream input, string contentType)
    {
        if (AttachmentPolicies.IsPdfContentType(contentType))
        {
            return FromPdf(input);
        }

        if ((contentType ?? string.Empty).Trim().StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return FromImage(input);
        }

        return new AttachmentThumbnailCodecResult(false, null, 0, 0, null, "UnsupportedContentType");
    }

    private static void ResizeToMaxEdge(Image image, int maxEdge)
    {
        var longest = Math.Max(image.Width, image.Height);
        if (longest <= maxEdge)
        {
            return;
        }

        var scale = maxEdge / (double)longest;
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        image.Mutate(ctx => ctx.Resize(width, height));
    }

    private static string Truncate(string message) =>
        message.Length <= 200 ? message : message[..200];
}

/// <summary>Loads original from object storage, generates WebP thumb, writes derivative (B-090).</summary>
public sealed class AttachmentThumbnailGenerator(
    IObjectStorage storage,
    ILogger<AttachmentThumbnailGenerator> logger)
{
    public async Task<bool> TryGenerateAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        if (!AttachmentPolicies.IsThumbnailEligible(attachment.ContentType))
        {
            attachment.ThumbnailStatus = null;
            return false;
        }

        attachment.ThumbnailStatus = ThumbnailStatus.Pending;

        await using var original = await storage.GetObjectAsync(attachment.StorageKey, cancellationToken);
        if (original is null)
        {
            attachment.ThumbnailStatus = ThumbnailStatus.Failed;
            logger.LogWarning("Thumbnail source missing for attachment {AttachmentId}", attachment.Id);
            return false;
        }

        var codec = AttachmentThumbnailCodec.FromContent(original, attachment.ContentType);
        attachment.Width = codec.Width > 0 ? codec.Width : attachment.Width;
        attachment.Height = codec.Height > 0 ? codec.Height : attachment.Height;
        if (codec.PageCount is int pages)
        {
            attachment.PageCount = pages;
        }

        if (!codec.Success || codec.WebpBytes is null || codec.WebpBytes.Length == 0)
        {
            attachment.ThumbnailStatus = ThumbnailStatus.Failed;
            logger.LogWarning(
                "Thumbnail generation failed for attachment {AttachmentId}: {Error}",
                attachment.Id,
                codec.Error ?? "Unknown");
            return false;
        }

        var thumbKey = AttachmentPolicies.BuildThumbnailKey(
            attachment.TenantId,
            attachment.ChannelId,
            attachment.Id);
        await using var upload = new MemoryStream(codec.WebpBytes, writable: false);
        await storage.PutObjectAsync(
            thumbKey,
            upload,
            AttachmentPolicies.ThumbnailContentType,
            cancellationToken);

        attachment.ThumbnailKey = thumbKey;
        attachment.ThumbnailStatus = ThumbnailStatus.Ready;
        return true;
    }
}
