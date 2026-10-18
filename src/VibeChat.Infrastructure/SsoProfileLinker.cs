using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

/// <summary>
/// Applies broker <c>(issuer, subject)</c> linking when SSO is enabled (B-164).
/// Returns null when the flag is off or the token has no broker claims, so the
/// existing Keycloak subject path stays unchanged.
/// </summary>
public static class SsoProfileLinker
{
    public static async Task<UserProfile?> TryLinkAsync(
        ClaimsPrincipal principal,
        VibeChatDbContext db,
        IClock clock,
        SsoLinkSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.Enabled || !SsoBrokerClaimsReader.TryRead(principal.Claims, out var broker))
        {
            return null;
        }

        var keycloakSubject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("Missing subject.");
        var email = (principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email") ?? string.Empty).Trim();
        var displayName = principal.FindFirstValue("name") ?? keycloakSubject;

        var profileBySubject = await db.UserProfiles.FirstOrDefaultAsync(x => x.Subject == keycloakSubject, cancellationToken);
        UserProfile? pending = null;
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.ToLowerInvariant();
            var pendingSubject = WorkspaceRolePolicies.PendingSubjectForEmail(normalizedEmail);
            pending = await db.UserProfiles.FirstOrDefaultAsync(
                x => x.Subject == pendingSubject
                    || (x.Email.ToLower() == normalizedEmail && x.Subject.StartsWith("pending:")),
                cancellationToken);
        }

        var link = await db.ExternalIdentities.FirstOrDefaultAsync(
            x => x.Issuer == broker.Issuer && x.Subject == broker.Subject,
            cancellationToken);

        var decision = SsoLinkPolicy.Decide(new SsoLinkFacts(
            profileBySubject?.Id.Value,
            profileBySubject is not null && WorkspaceRolePolicies.IsPendingSubject(profileBySubject.Subject),
            pending?.Id.Value,
            link?.UserId.Value));

        if (decision.Kind == SsoLinkKind.Conflict)
        {
            VibeChatMetrics.SsoLinkDenied.Add(1);
            throw new SsoLinkConflictException();
        }

        UserProfile profile;
        if (decision.Kind == SsoLinkKind.CreateUnprivileged)
        {
            profile = new UserProfile
            {
                Id = UserId.New(),
                Subject = keycloakSubject,
                Email = string.IsNullOrWhiteSpace(email) ? $"{keycloakSubject}@unknown.local" : email,
                DisplayName = displayName,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            db.UserProfiles.Add(profile);
        }
        else
        {
            var userId = new UserId(decision.UserId!.Value);
            profile = profileBySubject?.Id == userId
                ? profileBySubject
                : pending?.Id == userId
                    ? pending
                    : await db.UserProfiles.FirstAsync(x => x.Id == userId, cancellationToken);

            if (!string.Equals(profile.Subject, keycloakSubject, StringComparison.Ordinal))
            {
                var taken = await db.UserProfiles.AnyAsync(
                    x => x.Subject == keycloakSubject && x.Id != profile.Id,
                    cancellationToken);
                if (taken)
                {
                    VibeChatMetrics.SsoLinkDenied.Add(1);
                    throw new SsoLinkConflictException();
                }

                profile.Subject = keycloakSubject;
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                profile.Email = email;
            }

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                profile.DisplayName = displayName;
            }

            profile.UpdatedAt = clock.UtcNow;
        }

        if (link is null)
        {
            db.ExternalIdentities.Add(new ExternalIdentity
            {
                Id = Guid.NewGuid(),
                UserId = profile.Id,
                Issuer = broker.Issuer,
                Subject = broker.Subject,
                ProviderAlias = broker.Alias,
                Reason = decision.Reason,
                CreatedAt = clock.UtcNow
            });
            VibeChatMetrics.SsoLinkCreated.Add(1);
        }

        await db.SaveChangesAsync(cancellationToken);
        return profile;
    }
}
