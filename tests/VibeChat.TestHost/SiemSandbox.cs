using VibeChat.Integrations;

namespace VibeChat.TestHost;

/// <summary>In-process SIEM double. Never opens a socket.</summary>
public sealed class SiemSandbox
{
    public static readonly SiemSandbox Shared = new();
    private readonly object _gate = new();
    private readonly List<SiemCall> _calls = [];

    public string Mode { get; set; } = "ok";

    public int FailRemaining { get; set; }

    public IReadOnlyList<SiemCall> Calls
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
        FailRemaining = 0;
        lock (_gate)
        {
            _calls.Clear();
        }
    }

    public ConnectorHttpResult Send(ConnectorHttpRequest request)
    {
        lock (_gate)
        {
            _calls.Add(new SiemCall(request.Url, request.Body ?? string.Empty, Header(request, "X-VibeChat-Signature"), Header(request, "X-VibeChat-Delivery-Id")));
        }

        if (FailRemaining > 0)
        {
            FailRemaining--;
            return new ConnectorHttpResult(503, null, null, "UpstreamFailed");
        }

        if (string.Equals(Mode, "invalid", StringComparison.Ordinal))
        {
            return new ConnectorHttpResult(404, null, null, "UpstreamRejected");
        }

        if (string.Equals(Mode, "unavailable", StringComparison.Ordinal))
        {
            return new ConnectorHttpResult(0, null, null, "UpstreamFailed");
        }

        return new ConnectorHttpResult(204, null, null, null);
    }

    private static string? Header(ConnectorHttpRequest request, string name) =>
        request.Headers.TryGetValue(name, out var value) ? value : null;
}

public sealed record SiemCall(Uri Url, string Body, string? Signature, string? EventId);
