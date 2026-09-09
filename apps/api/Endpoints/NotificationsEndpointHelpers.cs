using VibeChat.Api;

namespace VibeChat.Api.Endpoints;

internal static class NotificationsEndpointHelpers
{
    internal static bool IsValidTimeZone(string timeZoneId)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>Next local midnight for "silenciar até amanhã" (B-097), converted back to UTC.</summary>
    internal static DateTimeOffset NextLocalMidnightUtc(DateTimeOffset nowUtc, string? timeZoneId)
    {
        var tz = TimeZoneInfo.Utc;
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                tz = TimeZoneInfo.Utc;
            }
        }

        var local = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var nextMidnightLocal = local.Date.AddDays(1);
        return new DateTimeOffset(nextMidnightLocal, tz.GetUtcOffset(nextMidnightLocal));
    }
}
