using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace VibeChat.Api.Authentication;

/// <summary>
/// B-109: bearer tokens prefixed <c>vc_int_</c> are not JWTs. This scheme
/// accepts the request without parsing the secret so JwtBearer does not log it.
/// The integration endpoints resolve the hash themselves.
/// </summary>
public sealed class IntegrationPassthroughHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Integration";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());
}
