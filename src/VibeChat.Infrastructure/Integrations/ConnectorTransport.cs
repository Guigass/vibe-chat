using System.Net;
using System.Text;
using VibeChat.Integrations;

namespace VibeChat.Infrastructure;

public sealed class DnsConnectorAddressLookup : IConnectorAddressLookup
{
    public IReadOnlyList<IPAddress> Lookup(string host)
    {
        if (ConnectorTestDoubles.Lookup is { } lookup)
        {
            return lookup.Lookup(host);
        }

        try
        {
            return Dns.GetHostAddresses(host);
        }
        catch (Exception)
        {
            return [];
        }
    }
}

public sealed class HttpConnectorTransport(IHttpClientFactory httpClientFactory) : IConnectorTransport
{
    public const string HttpClientName = "vibechat-connectors";

    public async Task<ConnectorHttpResult> SendAsync(ConnectorHttpRequest request, CancellationToken cancellationToken)
    {
        if (ConnectorTestDoubles.Transport is { } transport)
        {
            return await transport.SendAsync(request, cancellationToken);
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        if (request.Body is not null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, request.ContentType ?? "application/json");
        }

        foreach (var (name, value) in request.Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(name, value))
            {
                message.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }

        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 4096)
            {
                body = body[..4096];
            }

            return new ConnectorHttpResult((int)response.StatusCode, body, response.Headers.Location?.ToString(), null);
        }
        catch (Exception)
        {
            return new ConnectorHttpResult(0, null, null, "UpstreamFailed");
        }
    }
}
