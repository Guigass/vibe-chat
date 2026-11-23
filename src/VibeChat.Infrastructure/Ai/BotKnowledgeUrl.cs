using System.Net;
using VibeChat.AI;
using VibeChat.Integrations;

namespace VibeChat.Infrastructure.Ai;

public sealed record BotKnowledgeHttpResult(int Status, string? Location, byte[] Body, string ContentType);

public interface IBotKnowledgeDirectory
{
    Task<IReadOnlyList<IPAddress>> LookupAsync(string host, CancellationToken cancellationToken);
    Task<BotKnowledgeHttpResult> GetAsync(Uri url, int maxBytes, TimeSpan timeout, CancellationToken cancellationToken);
}

public static class BotKnowledgeUrlPolicy
{
    public static string? BlockReason(
        Uri url,
        IReadOnlyCollection<string> allowlist,
        IReadOnlyList<IPAddress> first,
        IReadOnlyList<IPAddress> second)
    {
        var decision = ConnectorPolicies.Authorize(url, first, second, allowlist);
        if (decision.Allowed)
        {
            return null;
        }

        return decision.ErrorCode == "DnsRebinding"
            ? BotKnowledgeErrors.DnsRebinding
            : BotKnowledgeErrors.EgressBlocked;
    }
}

public sealed class HttpBotKnowledgeDirectory : IBotKnowledgeDirectory
{
    public Task<IReadOnlyList<IPAddress>> LookupAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return Task.FromResult<IReadOnlyList<IPAddress>>([literal]);
        }

        return Task.Run(() => (IReadOnlyList<IPAddress>)Dns.GetHostAddresses(host), cancellationToken);
    }

    public async Task<BotKnowledgeHttpResult> GetAsync(Uri url, int maxBytes, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = timeout };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            return new BotKnowledgeHttpResult((int)response.StatusCode, response.Headers.Location?.ToString(), [], string.Empty);
        }

        var buffer = new byte[maxBytes + 1];
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        if (read > maxBytes)
        {
            return new BotKnowledgeHttpResult(413, null, [], string.Empty);
        }

        var type = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return new BotKnowledgeHttpResult((int)response.StatusCode, null, buffer[..read], type);
    }
}
