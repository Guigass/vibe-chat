using VibeChat.SharedKernel;

namespace VibeChat.Conversations;

public enum ChannelType
{
    Public = 0,
    Private = 1,
    Announcement = 2,
    Direct = 3,
    Group = 4,
    GroupDm = 5
}

public static class ChannelTypes
{
    public static bool RequiresChannelMembership(this ChannelType type) =>
        type is ChannelType.Private or ChannelType.Direct or ChannelType.Group or ChannelType.GroupDm;

    public static bool IsGroupDirect(this ChannelType type) =>
        type is ChannelType.GroupDm or ChannelType.Group;
}

public sealed class Channel : AggregateRoot
{
    public ChannelId Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid? SpaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional channel topic/description (B-087 /topico). Max 250 chars.</summary>
    public string? Topic { get; set; }
    /// <summary>Optional display title for a group DM (B-101). Internal <see cref="Name"/> stays unique.</summary>
    public string? Title { get; set; }
    /// <summary>Normalized sorted participant set for GroupDm get-or-create.</summary>
    public string? ParticipantSetKey { get; set; }
    public ChannelType Type { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public UserId CreatedBy { get; set; }
}

public sealed class ChannelMember : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public ChannelId ChannelId { get; set; }
    public UserId UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    /// <summary>Messages with <c>seq &lt;= JoinedSeq</c> are hidden (B-101). Founders use 0.</summary>
    public long JoinedSeq { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public long? LeftSeq { get; set; }
}

public static class GroupDmPolicies
{
    public const int MinParticipants = 3;
    public const int DefaultMaxParticipants = 9;
    public const int TitleMaxLength = 80;

    public static string ParticipantSetKey(IEnumerable<Guid> userIds)
    {
        var ordered = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .OrderBy(id => id)
            .Select(id => id.ToString("D"))
            .ToArray();
        return string.Join(',', ordered);
    }

    public static bool TryNormalizeMembers(
        IEnumerable<Guid> requestedUserIds,
        Guid callerUserId,
        int maxParticipants,
        out Guid[] members,
        out string error)
    {
        var set = new SortedSet<Guid>();
        if (callerUserId != Guid.Empty)
        {
            set.Add(callerUserId);
        }

        foreach (var id in requestedUserIds)
        {
            if (id != Guid.Empty)
            {
                set.Add(id);
            }
        }

        members = set.ToArray();
        if (members.Length < MinParticipants)
        {
            error = "GroupDmRequiresThree";
            return false;
        }

        if (members.Length > maxParticipants)
        {
            error = "GroupDmTooManyParticipants";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class GroupDmOptions
{
    public const string SectionName = "Directory:GroupDm";

    public bool Enabled { get; set; }
    public int MaxParticipants { get; set; } = GroupDmPolicies.DefaultMaxParticipants;
}

public interface IChannelMembershipReader
{
    Task<bool> CanAccessAsync(TenantId tenantId, ChannelId channelId, UserId userId, CancellationToken cancellationToken);
}
