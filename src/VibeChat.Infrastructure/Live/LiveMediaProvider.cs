using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VibeChat.Realtime;

namespace VibeChat.Infrastructure;

/// <summary>
/// Mints a short-lived room-scoped token. The API never relays media.
/// </summary>
public sealed class LiveMediaProvider(IOptions<LiveOptions> options, IHttpClientFactory http) : ILiveMediaProvider
{
    private readonly LiveOptions _options = options.Value;

    public string Name => LiveRules.NormalizeProvider(_options.Provider);

    public async Task<LiveProviderStatus> StatusAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new LiveProviderStatus(true, "disabled");
        }

        return Name switch
        {
            LiveRules.ProviderDown => new LiveProviderStatus(false, "down"),
            LiveRules.ProviderDev => SigningKey(LiveRules.LabSigningKey) is null
                ? new LiveProviderStatus(false, "signing-key")
                : new LiveProviderStatus(true, "dev"),
            LiveRules.ProviderLiveKit => await LiveKitStatusAsync(cancellationToken),
            _ => new LiveProviderStatus(false, "provider")
        };
    }

    public LiveAccessToken Issue(LiveTokenRequest request)
    {
        var secret = Name switch
        {
            LiveRules.ProviderDev => SigningKey(LiveRules.LabSigningKey),
            LiveRules.ProviderLiveKit => SigningKey(null),
            _ => null
        } ?? throw new InvalidOperationException("Live provider cannot issue a token.");

        var issuer = Name == LiveRules.ProviderLiveKit
            ? _options.LiveKit.ApiKey.Trim()
            : "vibechat-dev";
        var sources = LiveRules.PublishSources(request.CanPublishVideo, request.CanPublishScreen);
        var video = new Dictionary<string, object>
        {
            ["room"] = request.Room,
            ["roomJoin"] = true,
            ["canPublish"] = true,
            ["canSubscribe"] = true,
            ["canPublishSources"] = sources
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: issuer,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, request.Identity),
                new Claim(JwtRegisteredClaimNames.Jti, request.Jti),
                new Claim("vc_room", request.Room),
                new Claim("vc_audio", request.CanPublishAudio ? "1" : "0"),
                new Claim("vc_video", request.CanPublishVideo ? "1" : "0"),
                new Claim("vc_screen", request.CanPublishScreen ? "1" : "0")
            ],
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: request.ExpiresAt.UtcDateTime,
            signingCredentials: credentials);
        jwt.Payload["video"] = video;
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        var signalUrl = Name == LiveRules.ProviderLiveKit ? _options.LiveKit.Url.Trim() : null;
        return new LiveAccessToken(token, string.IsNullOrEmpty(signalUrl) ? null : signalUrl, request.Room, request.Jti, request.ExpiresAt);
    }

    public LiveTokenClaims? Read(string token)
    {
        var secret = Name switch
        {
            LiveRules.ProviderDev => SigningKey(LiveRules.LabSigningKey),
            LiveRules.ProviderLiveKit => SigningKey(null),
            _ => null
        };
        if (secret is null || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(5),
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                NameClaimType = JwtRegisteredClaimNames.Sub
            }, out _);
            var room = principal.FindFirst("vc_room")?.Value;
            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            var identity = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(room) || string.IsNullOrEmpty(jti) || string.IsNullOrEmpty(identity))
            {
                return null;
            }

            var jwt = handler.ReadJwtToken(token);
            return new LiveTokenClaims(
                jti,
                room,
                identity,
                principal.FindFirst("vc_audio")?.Value == "1",
                principal.FindFirst("vc_video")?.Value == "1",
                jwt.ValidTo == DateTime.MinValue ? DateTimeOffset.UtcNow : new DateTimeOffset(DateTime.SpecifyKind(jwt.ValidTo, DateTimeKind.Utc)),
                principal.FindFirst("vc_screen")?.Value == "1");
        }
        catch (SecurityTokenExpiredException)
        {
            return null;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }

    private async Task<LiveProviderStatus> LiveKitStatusAsync(CancellationToken cancellationToken)
    {
        if (SigningKey(null) is null
            || string.IsNullOrWhiteSpace(_options.LiveKit.Url)
            || string.IsNullOrWhiteSpace(_options.LiveKit.ApiKey))
        {
            return new LiveProviderStatus(false, "config");
        }

        try
        {
            var client = http.CreateClient(LiveRules.HttpClientName);
            using var response = await client.GetAsync(LiveRules.HttpProbeUrl(_options.LiveKit.Url), cancellationToken);
            return response.IsSuccessStatusCode
                ? new LiveProviderStatus(true, "livekit")
                : new LiveProviderStatus(false, "http");
        }
        catch (Exception)
        {
            return new LiveProviderStatus(false, "http");
        }
    }

    private string? SigningKey(string? fallback)
    {
        var configured = Name == LiveRules.ProviderLiveKit
            ? _options.LiveKit.ApiSecret
            : _options.DevSigningKey;
        var secret = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        if (string.IsNullOrEmpty(secret) || Encoding.UTF8.GetByteCount(secret) < LiveRules.SigningKeyMinBytes)
        {
            return null;
        }

        return secret;
    }
}
