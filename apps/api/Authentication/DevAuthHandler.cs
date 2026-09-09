using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using VibeChat.Api;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

public sealed class DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // B-177: no silent demo fallback — resolve name only from header/query.
        string? name = null;
        if (Request.Headers.TryGetValue("X-Dev-User", out var headerValues) && !string.IsNullOrWhiteSpace(headerValues))
        {
            name = headerValues.ToString();
        }
        else if (Request.Query.TryGetValue("devUser", out var queryValues) && !string.IsNullOrWhiteSpace(queryValues))
        {
            name = queryValues.ToString();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing X-Dev-User or devUser."));
        }

        var key = name.ToLowerInvariant();
        List<Claim> claims;
        if (key is "alice" or "bob" or "demo")
        {
            var (id, email, display) = key switch
            {
                "alice" => (SeedData.AliceUserId, "alice@vibechat.local", "Alice"),
                "bob" => (SeedData.BobUserId, "bob@vibechat.local", "Bob"),
                _ => (SeedData.DemoUserId, "demo@vibechat.local", "Demo")
            };

            claims =
            [
                new Claim(ClaimTypes.NameIdentifier, $"dev:{key}"),
                new Claim("sub", $"dev:{key}"),
                new Claim("vibechat_user_id", id.Value.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim("name", display),
                new Claim(ClaimTypes.Role, key == "demo" ? Role.WorkspaceOwner.ToString() : Role.Member.ToString())
            ];
        }
        else if (Request.Headers.TryGetValue("X-Dev-Email", out var emailValues)
                 && !string.IsNullOrWhiteSpace(emailValues))
        {
            // B-068 DX: dynamic invitee for pending:{email} claim tests (Development only).
            var email = emailValues.ToString().Trim().ToLowerInvariant();
            var display = Request.Headers.TryGetValue("X-Dev-Name", out var nameValues) && !string.IsNullOrWhiteSpace(nameValues)
                ? nameValues.ToString().Trim()
                : key;
            claims =
            [
                new Claim(ClaimTypes.NameIdentifier, $"dev:{key}"),
                new Claim("sub", $"dev:{key}"),
                new Claim(ClaimTypes.Email, email),
                new Claim("name", display),
                new Claim(ClaimTypes.Role, Role.Member.ToString())
            ];
        }
        else
        {
            // B-177: unknown X-Dev-User without X-Dev-Email → fail closed (401).
            return Task.FromResult(AuthenticateResult.Fail($"Unknown X-Dev-User '{key}'. Use alice|bob|demo or X-Dev-Email."));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
