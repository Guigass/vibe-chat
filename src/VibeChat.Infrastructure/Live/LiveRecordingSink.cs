using VibeChat.Files;
using VibeChat.Realtime;

namespace VibeChat.Infrastructure;

/// <summary>
/// Stores a sealed recording in object storage and returns only the object key.
/// </summary>
public sealed class LiveRecordingSink(IObjectStorage storage, IMalwareScanner scanner) : ILiveRecordingSink
{
    public async Task<LiveStoreResult> StoreAsync(string objectKey, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (LiveRules.IsPublicUrl(objectKey) || content.IsEmpty)
        {
            return new LiveStoreResult(false, null, 0, LiveRules.ScanRejected);
        }

        await using var stream = new MemoryStream(content.ToArray());
        var scan = await scanner.ScanAsync(stream, LiveRules.RecordingContentType, cancellationToken);
        if (scan.Status != AttachmentScanStatus.Clean)
        {
            return new LiveStoreResult(false, null, content.Length, LiveRules.ScanRejected);
        }

        stream.Position = 0;
        await storage.PutObjectAsync(objectKey, stream, LiveRules.RecordingContentType, cancellationToken);
        return new LiveStoreResult(true, objectKey, content.Length, LiveRules.ScanClean);
    }
}
