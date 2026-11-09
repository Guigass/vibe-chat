using System.Collections.Concurrent;

namespace VibeChat.Infrastructure.Messaging;

public readonly record struct CanvasPeer(Guid UserId, string Label, int Cursor);

/// <summary>Process-local cursors. They are not a security boundary and are not stored.</summary>
public sealed class CanvasPresence
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<(Guid PageId, Guid UserId), Entry> peers = new();

    public void Touch(Guid pageId, Guid userId, string label, int cursor, DateTimeOffset now)
    {
        peers[(pageId, userId)] = new Entry(label, cursor, now);
    }

    public IReadOnlyList<CanvasPeer> List(Guid pageId, DateTimeOffset now)
    {
        var visible = new List<CanvasPeer>();
        foreach (var pair in peers)
        {
            if (now - pair.Value.Seen > Ttl)
            {
                peers.TryRemove(pair.Key, out _);
                continue;
            }

            if (pair.Key.PageId == pageId)
            {
                visible.Add(new CanvasPeer(pair.Key.UserId, pair.Value.Label, pair.Value.Cursor));
            }
        }

        visible.Sort(static (left, right) => left.UserId.CompareTo(right.UserId));
        return visible;
    }

    private readonly record struct Entry(string Label, int Cursor, DateTimeOffset Seen);
}
