using System.Globalization;
using VibeChat.SharedKernel;

namespace VibeChat.Notifications;

public static class DigestCadence
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string OnDemand = "ondemand";
}

public static class DigestDelivery
{
    public const string InApp = "in_app";
    public const string Email = "email";
    public const string Push = "push";
}

public static class DigestRunStatus
{
    public const string Ready = "ready";
    public const string Partial = "partial";
    public const string Failed = "failed";
}

public static class DigestAiStatus
{
    public const string Off = "off";
    public const string Ready = "ready";
    public const string Failed = "failed";
}

public static class DigestItemKind
{
    public const string Unread = "unread";
    public const string Mention = "mention";
    public const string Announcement = "announcement";
    public const string Decision = "decision";
    public const string Task = "task";

    public static readonly string[] All = [Unread, Mention, Announcement, Decision, Task];
}

public static class DigestEventTypes
{
    public const string Generated = "digest.generated";
}

/// <summary>Personal schedule (B-122). One row per user. Snapshot runs live in <see cref="DigestRun"/>.</summary>
public sealed class DigestSchedule
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public bool Enabled { get; set; }
    public string Cadence { get; set; } = DigestCadence.Daily;
    public TimeOnly LocalTime { get; set; } = DigestPolicies.DefaultLocalTime;
    public short Weekday { get; set; } = 1;
    public string TimeZone { get; set; } = "UTC";
    public string Delivery { get; set; } = DigestDelivery.InApp;
    public bool IncludeAi { get; set; }
    public Guid[] ChannelIds { get; set; } = [];
    public DateTimeOffset? NextCheckAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One generated digest. Items store ids only — never a copy of the message body.</summary>
public sealed class DigestRun
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public string Cadence { get; set; } = DigestCadence.OnDemand;
    public string Status { get; set; } = DigestRunStatus.Ready;
    public string AiStatus { get; set; } = DigestAiStatus.Off;
    public string? Summary { get; set; }
    public string Delivery { get; set; } = DigestDelivery.InApp;
    public int DeliveryCount { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
}

public sealed class DigestItem
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid RunId { get; set; }
    public string Kind { get; set; } = DigestItemKind.Unread;
    public Guid SourceId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid? MessageId { get; set; }
    public long? Sequence { get; set; }
    public DateTimeOffset SortAt { get; set; }
}

public sealed record DigestSource(
    string Kind,
    Guid SourceId,
    Guid ChannelId,
    Guid? MessageId,
    long? Sequence,
    DateTimeOffset SortAt);

public readonly record struct DigestAiOutcome(string Status, string? Summary)
{
    public static DigestAiOutcome Off() => new(DigestAiStatus.Off, null);

    public static DigestAiOutcome Ready(string? text) =>
        new(DigestAiStatus.Ready, DigestPolicies.TrimSummary(text));

    public static DigestAiOutcome Failed() => new(DigestAiStatus.Failed, null);
}

public sealed record DigestDraft(
    IReadOnlyList<DigestSource> Items,
    string Status,
    string AiStatus,
    string? Summary);

public sealed record DigestVisibleItem(
    string Kind,
    Guid SourceId,
    Guid ChannelId,
    string ChannelName,
    Guid? MessageId,
    long? Sequence);

public sealed class DigestOptions
{
    public const string SectionName = "Notifications:Digests";

    public bool Enabled { get; set; }
}

public static class DigestPolicies
{
    public const int MaxPerKind = 30;
    public const int MaxChannels = 50;
    public const int MaxAttempts = 3;
    public const int BatchSize = 20;
    public const int SummaryMaxChars = 500;
    public const int PeriodKeyMaxLength = 80;
    public static readonly TimeOnly DefaultLocalTime = new(8, 0);

    public static bool TryParseCadence(string? value, out string cadence)
    {
        cadence = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return cadence is DigestCadence.Daily or DigestCadence.Weekly;
    }

    public static bool TryParseDelivery(string? value, out string delivery)
    {
        delivery = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return delivery is DigestDelivery.InApp or DigestDelivery.Email or DigestDelivery.Push;
    }

    public static bool TryNormalizeIdempotency(string? raw, out string key)
    {
        key = raw?.Trim() ?? string.Empty;
        if (key.Length is < 8 or > 80)
        {
            return false;
        }

        foreach (var character in key)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or ':'))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryTimeZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static string PeriodKey(string cadence, DateTime localDate, string? idempotency)
    {
        if (cadence == DigestCadence.OnDemand)
        {
            return $"ondemand:{idempotency}";
        }

        if (cadence == DigestCadence.Weekly)
        {
            var week = ISOWeek.GetWeekOfYear(localDate);
            var year = ISOWeek.GetYear(localDate);
            return $"weekly:{year}-W{week:00}";
        }

        return $"daily:{localDate:yyyy-MM-dd}";
    }

    public static bool IsDue(DigestSchedule schedule, DateTimeOffset nowUtc)
    {
        if (!schedule.Enabled || !TryTimeZone(schedule.TimeZone, out var zone))
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        if (schedule.Cadence == DigestCadence.Weekly)
        {
            var today = (int)local.DayOfWeek;
            if (today < schedule.Weekday)
            {
                return false;
            }

            if (today == schedule.Weekday)
            {
                return time >= schedule.LocalTime;
            }

            return true;
        }

        return time >= schedule.LocalTime;
    }

    public static DateTimeOffset NextCheckUtc(DigestSchedule schedule, DateTimeOffset nowUtc, bool deferredForDnd)
    {
        if (deferredForDnd || !TryTimeZone(schedule.TimeZone, out var zone))
        {
            return nowUtc.AddMinutes(15);
        }

        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var slot = local.Date + schedule.LocalTime.ToTimeSpan();
        if (schedule.Cadence == DigestCadence.Weekly)
        {
            var days = ((int)schedule.Weekday - (int)local.DayOfWeek + 7) % 7;
            if (days == 0 && TimeOnly.FromDateTime(local.DateTime) >= schedule.LocalTime)
            {
                days = 7;
            }

            slot = local.Date.AddDays(days) + schedule.LocalTime.ToTimeSpan();
        }
        else if (TimeOnly.FromDateTime(local.DateTime) >= schedule.LocalTime)
        {
            slot = slot.AddDays(1);
        }

        var unspecified = DateTime.SpecifyKind(slot, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), TimeSpan.Zero);
    }

    public static IReadOnlyList<T> Cap<T>(IEnumerable<T> items) =>
        items.Take(MaxPerKind).ToArray();

    public static string AiPrompt(int unread, int mention, int announcement, int decision, int task) =>
        $"unread={unread}\nmention={mention}\nannouncement={announcement}\ndecision={decision}\ntask={task}";

    public static string? TrimSummary(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.Length <= SummaryMaxChars ? trimmed : trimmed[..SummaryMaxChars];
    }

    public static DigestDraft Draft(IReadOnlyList<DigestSource> items, DigestAiOutcome ai, bool externalDeferred)
    {
        var status = ai.Status == DigestAiStatus.Failed || externalDeferred
            ? DigestRunStatus.Partial
            : DigestRunStatus.Ready;
        return new DigestDraft(items, status, ai.Status, ai.Status == DigestAiStatus.Ready ? ai.Summary : null);
    }

    public static bool ShouldSendExternal(string delivery, int itemCount, bool withinDnd, bool transportEnabled) =>
        itemCount > 0
        && !withinDnd
        && transportEnabled
        && delivery is DigestDelivery.Email or DigestDelivery.Push;

    public static bool ShouldDeferExternal(string delivery, int itemCount, bool withinDnd) =>
        itemCount > 0
        && withinDnd
        && delivery is DigestDelivery.Email or DigestDelivery.Push;

    public static string ExternalNotice(string? locale, int unread, int mention, int announcement, int decision, int task)
    {
        var english = locale is not null && locale.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        return english
            ? $"{unread} unread, {mention} mentions, {announcement} announcements, {decision} decisions, {task} tasks"
            : $"{unread} não lidas, {mention} menções, {announcement} anúncios, {decision} decisões, {task} tarefas";
    }

    public static string ExternalTitle(string? locale) =>
        locale is not null && locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "Catch-up"
            : "Resumo";
}

public static class DigestAiGate
{
    public static async Task<DigestAiOutcome> RunAsync(
        Func<CancellationToken, Task<string?>> complete,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return DigestAiOutcome.Off();
        }

        try
        {
            var text = await complete(cancellationToken);
            var summary = DigestPolicies.TrimSummary(text);
            return summary is null ? DigestAiOutcome.Failed() : DigestAiOutcome.Ready(summary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DigestAiOutcome.Failed();
        }
    }
}
