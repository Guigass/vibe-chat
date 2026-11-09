public sealed record PostCanvasUpdateRequest(
    int WireVersion,
    string IdempotencyKey,
    int Epoch,
    string UpdateBase64);

public sealed record PostCanvasPresenceRequest(int Cursor);

public sealed record PostCanvasRestoreRequest(Guid SnapshotId);

public sealed record CanvasPeerResponse(Guid UserId, string Label, int Cursor);

public sealed record CanvasSnapshotResponse(Guid Id, long Seq, int Epoch, DateTimeOffset CreatedAt);

public sealed record CanvasStateResponse(
    int WireVersion,
    int Epoch,
    long Seq,
    long CompactedThroughSeq,
    string StateBase64,
    string Body,
    IReadOnlyList<CanvasPeerResponse> Peers,
    IReadOnlyList<CanvasSnapshotResponse> Snapshots);

public sealed record CanvasUpdateItemResponse(long Seq, int Epoch, Guid AuthorUserId, string UpdateBase64);

public sealed record CanvasUpdateListResponse(int Epoch, long CompactedThroughSeq, IReadOnlyList<CanvasUpdateItemResponse> Items);

public sealed record CanvasWriteResponse(int Epoch, long Seq, string Body, string StateBase64, bool Replayed);

public sealed record CanvasExportSummary(
    int Epoch,
    long Seq,
    int UpdateCount,
    IReadOnlyList<Guid> SnapshotIds,
    string StateSha256);
