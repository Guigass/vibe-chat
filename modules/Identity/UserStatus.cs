using VibeChat.SharedKernel;

namespace VibeChat.Identity;

/// <summary>Allowlisted custom status (B-116). Availability is derived, not stored.</summary>
public enum UserStatusState
{
    Focus = 1,
    Meeting = 2,
    Vacation = 3,
    Custom = 4
}

/// <summary>Derived from presence, DND and status. No schedule payload.</summary>
public enum AvailabilityKind
{
    Available = 0,
    Away = 1,
    Busy = 2,
    Vacation = 3,
    Offline = 4
}

/// <summary>Persistent status for one user inside one tenant (B-116).</summary>
public sealed class UserStatus
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public UserStatusState State { get; set; }
    public string Emoji { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool ClearAtEndOfDay { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Future calendar hook (B-116). Off by default; this slice never syncs or returns agenda rows.
/// </summary>
public sealed class AvailabilityCalendarOptions
{
    public const string SectionName = "Features:AvailabilityCalendar";

    public bool Enabled { get; set; }
}

public static class UserStatusRules
{
    public const int MaxTextLength = 80;
    public const int MaxEmojiLength = 16;

    public static bool TryNormalize(
        string? state,
        string? emoji,
        string? text,
        out UserStatusState parsedState,
        out string normalizedEmoji,
        out string normalizedText,
        out string error)
    {
        parsedState = default;
        normalizedEmoji = string.Empty;
        normalizedText = string.Empty;
        error = string.Empty;

        if (!TryParseState(state, out parsedState))
        {
            error = "InvalidStatusState";
            return false;
        }

        if (!TryNormalizeEmoji(emoji, out normalizedEmoji, out error))
        {
            return false;
        }

        if (!TryNormalizeText(text, out normalizedText, out error))
        {
            return false;
        }

        if (parsedState == UserStatusState.Custom
            && normalizedEmoji.Length == 0
            && normalizedText.Length == 0)
        {
            error = "StatusContentRequired";
            return false;
        }

        if (normalizedEmoji.Length == 0)
        {
            normalizedEmoji = DefaultEmoji(parsedState);
        }

        return true;
    }

    public static bool TryParseState(string? raw, out UserStatusState state)
    {
        state = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (!Enum.TryParse(raw.Trim(), ignoreCase: true, out state) || !Enum.IsDefined(state))
        {
            return false;
        }

        return true;
    }

    public static DateTimeOffset? ResolveExpiresAt(
        DateTimeOffset? explicitExpiresAt,
        bool clearAtEndOfDay,
        string? timeZoneId,
        DateTimeOffset nowUtc,
        out string error)
    {
        error = string.Empty;
        if (explicitExpiresAt is { } chosen && chosen <= nowUtc)
        {
            error = "StatusExpiresInPast";
            return null;
        }

        DateTimeOffset? endOfDay = clearAtEndOfDay ? EndOfLocalDay(nowUtc, timeZoneId) : null;
        if (explicitExpiresAt is null)
        {
            return endOfDay;
        }

        if (endOfDay is null || explicitExpiresAt.Value <= endOfDay.Value)
        {
            return explicitExpiresAt;
        }

        return endOfDay;
    }

    public static DateTimeOffset EndOfLocalDay(DateTimeOffset nowUtc, string? timeZoneId)
    {
        var tz = ResolveTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var nextMidnight = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(nextMidnight, tz);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public static bool IsExpired(DateTimeOffset? expiresAt, DateTimeOffset nowUtc) =>
        expiresAt is { } exp && exp <= nowUtc;

    public static bool IsActive(UserStatus? status, DateTimeOffset nowUtc) =>
        status is not null && !IsExpired(status.ExpiresAt, nowUtc);

    /// <summary>
    /// DND wins over status for availability (busy). Vacation/focus/meeting otherwise beat raw presence.
    /// Custom text does not change availability by itself.
    /// </summary>
    public static AvailabilityKind Derive(
        string? presence,
        bool dndActive,
        UserStatusState? state,
        bool statusActive)
    {
        if (dndActive)
        {
            return AvailabilityKind.Busy;
        }

        if (statusActive)
        {
            if (state == UserStatusState.Vacation)
            {
                return AvailabilityKind.Vacation;
            }

            if (state is UserStatusState.Focus or UserStatusState.Meeting)
            {
                return AvailabilityKind.Busy;
            }
        }

        return (presence ?? "offline").Trim().ToLowerInvariant() switch
        {
            "online" => AvailabilityKind.Available,
            "away" => AvailabilityKind.Away,
            _ => AvailabilityKind.Offline
        };
    }

    /// <summary>
    /// Custom status never bypasses DND. The only exception remains a priority DM (B-097).
    /// Push dispatch applies DND on its own and does not read this status.
    /// </summary>
    public static bool NotificationSuppressedByDnd(bool dndActive, bool priorityBypass) =>
        dndActive && !priorityBypass;

    public static string ToApiState(UserStatusState state) => state.ToString().ToLowerInvariant();

    public static string ToApiAvailability(AvailabilityKind availability) =>
        availability.ToString().ToLowerInvariant();

    public static string ToApiPresence(string? presence)
    {
        var value = (presence ?? "offline").Trim().ToLowerInvariant();
        return value is "online" or "away" ? value : "offline";
    }

    private static string DefaultEmoji(UserStatusState state) => state switch
    {
        UserStatusState.Focus => "🎯",
        UserStatusState.Meeting => "📅",
        UserStatusState.Vacation => "🌴",
        _ => string.Empty
    };

    private static bool TryNormalizeEmoji(string? emoji, out string normalized, out string error)
    {
        normalized = (emoji ?? string.Empty).Trim();
        error = string.Empty;
        if (normalized.Length == 0)
        {
            return true;
        }

        if (normalized.Length > MaxEmojiLength || HasControl(normalized))
        {
            error = "StatusEmojiTooLong";
            return false;
        }

        return true;
    }

    private static bool TryNormalizeText(string? text, out string normalized, out string error)
    {
        normalized = (text ?? string.Empty).Trim();
        error = string.Empty;
        if (normalized.Length == 0)
        {
            return true;
        }

        if (HasControl(normalized))
        {
            error = "StatusTextInvalid";
            return false;
        }

        if (normalized.Length > MaxTextLength)
        {
            error = "StatusTextTooLong";
            return false;
        }

        return true;
    }

    private static bool HasControl(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
