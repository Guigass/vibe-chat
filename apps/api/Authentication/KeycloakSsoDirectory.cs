using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VibeChat.Identity;

namespace VibeChat.Api.Authentication;

/// <summary>
/// Reads discoverable Keycloak identity providers. Failure returns an empty list
/// so the Keycloak button still works (B-164).
/// </summary>
public sealed class KeycloakSsoDirectory(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<SsoOptions> options,
    ILogger<KeycloakSsoDirectory> logger)
{
    public const string HttpClientName = "sso-directory";
    private readonly object _gate = new();
    private readonly SsoDirectoryCircuit _circuit = new();
    private IReadOnlyList<SsoProvider> _cached = [];
    private DateTimeOffset _cachedUntil;
    private string? _accessToken;
    private DateTimeOffset _accessTokenUntil;

    public async Task<IReadOnlyList<SsoProvider>> ListAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (now < _cachedUntil)
            {
                return _cached;
            }

            if (_circuit.IsOpen(now))
            {
                return [];
            }
        }

        var current = options.CurrentValue;
        if (!TryBuildUrls(current, out var tokenUrl, out var providersUrl))
        {
            return [];
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(current.TimeoutSeconds, 1, 5));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var token = await AccessTokenAsync(current, tokenUrl, timeoutCts.Token);
            if (token is null)
            {
                RecordFailure();
                return [];
            }

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, providersUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("SSO directory list failed with status {StatusCode}", (int)response.StatusCode);
                RecordFailure();
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var providers = SsoProviderParser.Parse(json);
            lock (_gate)
            {
                _cached = providers;
                _cachedUntil = DateTimeOffset.UtcNow.AddSeconds(30);
                _circuit.RecordSuccess();
            }

            return providers;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("SSO directory list timed out");
            RecordFailure();
            return [];
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "SSO directory list failed");
            RecordFailure();
            return [];
        }
    }

    private async Task<string?> AccessTokenAsync(SsoOptions current, string tokenUrl, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_accessToken is not null && now < _accessTokenUntil)
            {
                return _accessToken;
            }
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = current.DirectoryClientId!.Trim(),
            ["client_secret"] = current.DirectoryClientSecret!
        });
        using var response = await client.PostAsync(tokenUrl, body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("SSO directory token failed with status {StatusCode}", (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("access_token", out var tokenElement)
            || tokenElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var token = tokenElement.GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var lifetime = 60;
        if (document.RootElement.TryGetProperty("expires_in", out var expires)
            && expires.TryGetInt32(out var seconds))
        {
            lifetime = Math.Clamp(seconds - 15, 15, 300);
        }

        lock (_gate)
        {
            _accessToken = token;
            _accessTokenUntil = DateTimeOffset.UtcNow.AddSeconds(lifetime);
        }

        return token;
    }

    private void RecordFailure()
    {
        lock (_gate)
        {
            _circuit.RecordFailure(DateTimeOffset.UtcNow);
            _cachedUntil = default;
            _accessToken = null;
        }
    }

    internal static bool TryBuildUrls(SsoOptions options, out string tokenUrl, out string providersUrl)
    {
        tokenUrl = string.Empty;
        providersUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(options.KeycloakBaseUrl)
            || string.IsNullOrWhiteSpace(options.DirectoryClientId)
            || string.IsNullOrWhiteSpace(options.DirectoryClientSecret)
            || string.IsNullOrWhiteSpace(options.Realm))
        {
            return false;
        }

        if (!Uri.TryCreate(options.KeycloakBaseUrl.Trim(), UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var realm = options.Realm.Trim();
        foreach (var ch in realm)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not '_' and not '-')
            {
                return false;
            }
        }

        var root = baseUri.ToString().TrimEnd('/');
        tokenUrl = $"{root}/realms/{realm}/protocol/openid-connect/token";
        providersUrl = $"{root}/admin/realms/{realm}/identity-provider/instances";
        return true;
    }
}
