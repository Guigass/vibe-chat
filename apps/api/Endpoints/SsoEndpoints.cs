using Microsoft.Extensions.Options;
using VibeChat.Api.Authentication;

namespace VibeChat.Api.Endpoints;

internal static class SsoEndpoints
{
    public static void MapSso(this WebApplication app)
    {
        app.MapGet("/api/v1/auth/sso-providers", async (
            IOptionsMonitor<SsoOptions> options,
            KeycloakSsoDirectory directory,
            CancellationToken ct) =>
        {
            if (!options.CurrentValue.Enabled)
            {
                return Results.Json(new { code = "SsoDisabled" }, statusCode: StatusCodes.Status404NotFound);
            }

            var items = await directory.ListAsync(ct);
            return Results.Ok(new
            {
                items = items.Select(item => new
                {
                    alias = item.Alias,
                    displayName = item.DisplayName,
                    domains = item.Domains
                })
            });
        }).AllowAnonymous();
    }
}
