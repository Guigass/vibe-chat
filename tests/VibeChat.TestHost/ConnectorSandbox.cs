using System.Collections.Concurrent;
using System.Net;
using VibeChat.Integrations;

namespace VibeChat.TestHost;

/// <summary>
/// In-process connector double. Never opens a socket. DNS answers are scripted.
/// </summary>
public sealed class ConnectorSandbox : IConnectorTransport, IConnectorAddressLookup
{
    public static readonly ConnectorSandbox Shared = new();

    public const string AccessToken = "sandbox-access-token-value";
    public const string RefreshToken = "sandbox-refresh-token-value";

    private readonly ConcurrentDictionary<string, int> _lookups = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly List<Uri> _calls = [];

    public string Mode { get; set; } = "ok";

    public IReadOnlyList<Uri> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    public void Reset()
    {
        Mode = "ok";
        _lookups.Clear();
        lock (_gate)
        {
            _calls.Clear();
        }
    }

    public IReadOnlyList<IPAddress> Lookup(string host)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }

        var count = _lookups.AddOrUpdate(host, 1, (_, current) => current + 1);
        if (host.Contains("rebind", StringComparison.OrdinalIgnoreCase))
        {
            return count % 2 == 1
                ? [IPAddress.Parse("1.1.1.1")]
                : [IPAddress.Parse("169.254.169.254")];
        }

        if (host.Contains("metadata", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return [IPAddress.Parse("169.254.169.254")];
        }

        return [IPAddress.Parse("1.1.1.1")];
    }

    public Task<ConnectorHttpResult> SendAsync(ConnectorHttpRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _calls.Add(request.Url);
        }

        if (string.Equals(Mode, "fail", StringComparison.Ordinal))
        {
            return Task.FromResult(new ConnectorHttpResult(0, null, null, "UpstreamFailed"));
        }

        if (request.Url.AbsolutePath.Contains("access_token", StringComparison.Ordinal)
            || request.Url.AbsolutePath.Contains("/oauth/token", StringComparison.Ordinal))
        {
            var scope = request.Url.Host.Contains("gitlab", StringComparison.OrdinalIgnoreCase)
                ? "read_api write_repository"
                : "issues:read issues:write";
            var body = $$"""{"access_token":"{{AccessToken}}","refresh_token":"{{RefreshToken}}","scope":"{{scope}}"}""";
            return Task.FromResult(new ConnectorHttpResult(200, body, null, null));
        }

        if (string.Equals(Mode, "redirect-metadata", StringComparison.Ordinal))
        {
            return Task.FromResult(new ConnectorHttpResult(302, null, "https://169.254.169.254/latest", null));
        }

        return Task.FromResult(new ConnectorHttpResult(204, null, null, null));
    }
}
