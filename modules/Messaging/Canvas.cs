using VibeChat.SharedKernel;

namespace VibeChat.Messaging;

/// <summary>
/// Collaborative page state (B-152). The markdown body is a Yjs text CRDT.
/// Title stays on the server-authoritative page row.
/// </summary>
public sealed class CanvasDocument
{
    public Guid PageId { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public int Epoch { get; set; } = 1;
    public long Seq { get; set; }
    public long CompactedThroughSeq { get; set; }
    public byte[] State { get; set; } = [];
    public UserId UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CanvasUpdate
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid PageId { get; set; }
    public int Epoch { get; set; }
    public long Seq { get; set; }
    public UserId AuthorUserId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CanvasSnapshot
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid PageId { get; set; }
    public int Epoch { get; set; }
    public long Seq { get; set; }
    public int PageVersion { get; set; }
    public byte[] State { get; set; } = [];
    public string Body { get; set; } = string.Empty;
    public UserId ActorUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CanvasOptions
{
    public const string SectionName = "Features:CanvasRealtime";

    public bool Enabled { get; set; }
}

public static class CanvasPolicies
{
    public const int WireVersion = 1;
    public const ulong ServerClientId = 42;
    public const int MaxUpdateBytes = 48_000;
    public const int MaxStateBytes = 64_000;
    public const int CompactEvery = 20;
    public const int MaxSnapshots = 30;
    public const int MaxLiveUpdates = 200;
    public const int MaxLabelLength = 80;
    public const string TextName = "body";
    public const string ChangedEvent = "knowledge.canvas.updated";
    public const string ActionSnapshot = "canvas-snapshot";
    public const string ActionRestored = "canvas-restored";

    public static bool IsCanvasEvent(string? type) =>
        string.Equals(type, ChangedEvent, StringComparison.Ordinal);

    public static string Label(string? displayName, Guid userId)
    {
        var chars = (displayName ?? string.Empty).Where(ch => !char.IsControl(ch)).ToArray();
        var value = new string(chars).Trim();
        if (value.Length > MaxLabelLength)
        {
            value = value[..MaxLabelLength];
        }

        return value.Length == 0 ? userId.ToString("N")[..8] : value;
    }
}
