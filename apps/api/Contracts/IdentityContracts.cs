public sealed record MeResponse(Guid UserId, string Subject, string Email, string DisplayName, string[] Roles, string? Locale = null);
public sealed record UpdateMeRequest(string? Locale);

public sealed record SetUserStatusRequest(
    string? State,
    string? Emoji,
    string? Text,
    bool ClearAtEndOfDay = false,
    DateTimeOffset? ExpiresAt = null);

public sealed record UserStatusBody(
    string State,
    string Emoji,
    string Text,
    bool ClearAtEndOfDay,
    DateTimeOffset? ExpiresAt);

public sealed record UserStatusResponse(string Presence, string Availability, UserStatusBody? Status);

public sealed record MemberAvailabilityResponse(
    Guid UserId,
    string Presence,
    string Availability,
    UserStatusBody? Status);

/// <summary>Calendar hook (B-116). Never includes events or schedule fields.</summary>
public sealed record CalendarAvailabilityResponse(bool Enabled, bool Connected);
