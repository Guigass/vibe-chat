public sealed record MeResponse(
    Guid UserId,
    string Subject,
    string Email,
    string DisplayName,
    string[] Roles,
    string? Locale = null,
    string? ChatWallpaperId = null,
    string? AccentColorId = null);

public sealed record UpdateMeRequest(string? Locale);

public sealed record UpdateMemberProfileRequest(
    string? DisplayName,
    string? JobTitle,
    string? About,
    string? HighlightMessage);

public sealed record MemberProfileResponse(
    Guid UserId,
    string DisplayName,
    string? Email,
    string? JobTitle,
    string? About,
    string? HighlightMessage,
    string? AvatarUrl);

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

public sealed record UpdateAppearanceRequest(string? ChatWallpaperId, string? AccentColorId);

public sealed record AppearanceResponse(string? ChatWallpaperId, string? AccentColorId);
