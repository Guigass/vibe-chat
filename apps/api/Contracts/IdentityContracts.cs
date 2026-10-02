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

public sealed record UpdateAppearanceRequest(string? ChatWallpaperId, string? AccentColorId);

public sealed record AppearanceResponse(string? ChatWallpaperId, string? AccentColorId);
