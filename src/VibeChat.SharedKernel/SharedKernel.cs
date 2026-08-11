namespace VibeChat.SharedKernel;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

public abstract class AggregateRoot : Entity;

public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.NewGuid());
    public static TenantId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());
    public static UserId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}

public readonly record struct WorkspaceId(Guid Value)
{
    public static WorkspaceId New() => new(Guid.NewGuid());
    public static WorkspaceId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}

public readonly record struct ChannelId(Guid Value)
{
    public static ChannelId New() => new(Guid.NewGuid());
    public static ChannelId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}

public readonly record struct MessageId(Guid Value)
{
    public static MessageId New() => new(Guid.NewGuid());
    public static MessageId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}

public sealed record Error(string Code, string Message)
{
    public static Error None => new("None", string.Empty);
}

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => new(value, true, Error.None);
    public static Result<T> Failure<T>(Error error) => new(default, false, error);
}

public sealed class Result<T> : Result
{
    internal Result(T? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Masks secrets for admin/API responses (B-069). Never log or return clear values.</summary>
public static class SecretMasking
{
    public static bool IsConfigured(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value.Trim(), "CHANGE_ME", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns null when not configured; otherwise ••••last4 (or •••• when too short).</summary>
    public static string? Mask(string? value)
    {
        if (!IsConfigured(value))
        {
            return null;
        }

        var trimmed = value!.Trim();
        return trimmed.Length <= 4 ? "••••" : $"••••{trimmed[^4..]}";
    }

    public static string Suffix(string? value)
    {
        if (!IsConfigured(value))
        {
            return string.Empty;
        }

        var trimmed = value!.Trim();
        return trimmed.Length <= 4 ? trimmed : trimmed[^4..];
    }

    public static string? MaskFromSuffix(string? suffix) =>
        string.IsNullOrWhiteSpace(suffix) ? null : $"••••{suffix.Trim()}";
}

/// <summary>AES-GCM envelope for external credentials at rest (ADR-020). Never log these bytes.</summary>
public sealed class EncryptedSecretEnvelope
{
    public const short CurrentFormatVersion = 1;
    public const int NonceLength = 12;
    public const int TagLength = 16;

    public byte[]? Ciphertext { get; set; }
    public byte[]? Nonce { get; set; }
    public byte[]? Tag { get; set; }
    public int? KeyVersion { get; set; }
    public short? FormatVersion { get; set; }
    public string? MaskSuffix { get; set; }
    public DateTimeOffset? RotatedAt { get; set; }

    public bool IsPresent =>
        Ciphertext is { Length: > 0 }
        && Nonce is { Length: NonceLength }
        && Tag is { Length: TagLength }
        && KeyVersion is > 0
        && FormatVersion is > 0;

    public void Clear()
    {
        Ciphertext = null;
        Nonce = null;
        Tag = null;
        KeyVersion = null;
        FormatVersion = null;
        MaskSuffix = null;
        RotatedAt = null;
    }

    public void CopyFrom(EncryptedSecretEnvelope source)
    {
        Ciphertext = source.Ciphertext is null ? null : source.Ciphertext.ToArray();
        Nonce = source.Nonce is null ? null : source.Nonce.ToArray();
        Tag = source.Tag is null ? null : source.Tag.ToArray();
        KeyVersion = source.KeyVersion;
        FormatVersion = source.FormatVersion;
        MaskSuffix = source.MaskSuffix;
        RotatedAt = source.RotatedAt;
    }
}

public static class Permissions
{
    public static class Workspace
    {
        public const string Read = "workspace.read";
        public const string Manage = "workspace.manage";
        public const string Admin = "workspace.admin";
    }

    public static class Channel
    {
        public const string Read = "channel.read";
        public const string Create = "channel.create";
        public const string Manage = "channel.manage";
        public const string MentionAll = "channel.mention_all";
    }

    public static class Message
    {
        public const string Read = "message.read";
        public const string Send = "message.send";
        public const string React = "message.react";
        public const string EditOwn = "message.edit.own";
        public const string DeleteOwn = "message.delete.own";
        public const string DeleteAny = "message.delete.any";
        public const string Pin = "message.pin";
    }

    public static class Files
    {
        public const string Upload = "file.upload";
        public const string Download = "file.download";
    }

    public static class Search
    {
        public const string Messages = "search.messages";
    }

    public static class Admin
    {
        public const string Dashboard = "admin.dashboard";
    }

    public static class Ai
    {
        public const string Summarize = "ai.summarize";
        public const string SuggestReply = "ai.suggest_reply";
        public const string Transcribe = "ai.transcribe";
    }
}

public enum Role
{
    PlatformOwner = 0,
    WorkspaceOwner = 1,
    Admin = 2,
    Moderator = 3,
    Auditor = 4,
    Member = 5,
    Guest = 6,
    Bot = 7
}
