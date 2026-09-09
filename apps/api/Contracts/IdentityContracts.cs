public sealed record MeResponse(Guid UserId, string Subject, string Email, string DisplayName, string[] Roles, string? Locale = null);
public sealed record UpdateMeRequest(string? Locale);
