using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VibeChat.Messaging;

namespace VibeChat.Infrastructure;

public sealed record LinkPreviewFetchResult(
    LinkPreviewStatus Status,
    string? Title,
    string? Description,
    string? SiteName,
    byte[]? ImageBytes,
    string? ImageContentType,
    string? Error);

/// <summary>SSRF-safe outbound fetch for B-091 link preview (scheme allowlist, IP guard after DNS and redirects).</summary>
public sealed class LinkPreviewFetcher(IHttpClientFactory httpClientFactory, ILogger<LinkPreviewFetcher> logger)
{
    public const string HttpClientName = "LinkPreview";

    public async Task<LinkPreviewFetchResult> FetchAsync(
        string normalizedUrl,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (LinkPreviewPolicies.NormalizeUrl(normalizedUrl) is not { } url)
        {
            return Blocked("InvalidOrDisallowedScheme");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Blocked("InvalidUri");
        }

        if (LinkPreviewPolicies.TryParseHostAsIp(uri.Host, out var literalIp)
            && LinkPreviewPolicies.IsBlockedIp(literalIp))
        {
            return Blocked("PrivateOrMetadataIp");
        }

        try
        {
            var resolved = await ResolveSafeAddressesAsync(uri.Host, cancellationToken);
            if (resolved.Length == 0)
            {
                return Blocked("NoPublicDnsAddress");
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "DNS resolve failed for link preview host {Host}", uri.Host);
            return Failed("DnsResolveFailed");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Math.Clamp(timeoutMs, 500, 15_000));

        try
        {
            var htmlResult = await FetchDocumentAsync(uri, timeoutCts.Token);
            if (htmlResult.Status != LinkPreviewStatus.Ready || string.IsNullOrEmpty(htmlResult.Html))
            {
                return htmlResult.Status == LinkPreviewStatus.Blocked
                    ? Blocked(htmlResult.Error ?? "Blocked")
                    : Failed(htmlResult.Error ?? "FetchFailed");
            }

            var meta = OpenGraphParser.Parse(htmlResult.Html, uri);
            if (string.IsNullOrWhiteSpace(meta.Title)
                && string.IsNullOrWhiteSpace(meta.Description)
                && string.IsNullOrWhiteSpace(meta.ImageUrl))
            {
                return new LinkPreviewFetchResult(LinkPreviewStatus.Failed, null, null, null, null, null, "NoOpenGraph");
            }

            byte[]? imageBytes = null;
            string? imageContentType = null;
            if (!string.IsNullOrWhiteSpace(meta.ImageUrl)
                && ResolveImageUri(uri, meta.ImageUrl) is { } imageUri)
            {
                // Separate budget — HTML fetch must not starve the thumbnail download.
                using var imageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                imageCts.CancelAfter(Math.Clamp(timeoutMs, 500, 15_000));
                var image = await FetchImageAsync(imageUri, imageCts.Token);
                if (image.Status == LinkPreviewStatus.Ready && image.ImageBytes is { Length: > 0 })
                {
                    imageBytes = image.ImageBytes;
                    imageContentType = image.ImageContentType;
                }
                else
                {
                    logger.LogWarning(
                        "Link preview image skipped for {Url}: {Status} {Error}",
                        meta.ImageUrl,
                        image.Status,
                        image.Error);
                }
            }

            return new LinkPreviewFetchResult(
                LinkPreviewStatus.Ready,
                LinkPreviewPolicies.Truncate(meta.Title, LinkPreviewPolicies.MaxTitleLength),
                LinkPreviewPolicies.Truncate(meta.Description, LinkPreviewPolicies.MaxDescriptionLength),
                LinkPreviewPolicies.Truncate(meta.SiteName, LinkPreviewPolicies.MaxSiteNameLength),
                imageBytes,
                imageContentType,
                null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("Timeout");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Link preview fetch failed for {Url}", url);
            return Failed(TruncateError(ex.Message));
        }
    }

    private async Task<(LinkPreviewStatus Status, string? Html, string? Error)> FetchDocumentAsync(
        Uri startUri,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var current = startUri;
        for (var redirect = 0; redirect <= LinkPreviewPolicies.MaxRedirects; redirect++)
        {
            if (!await EnsureHostSafeAsync(current.Host, cancellationToken))
            {
                return (LinkPreviewStatus.Blocked, null, "PrivateOrMetadataIp");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location is null || redirect == LinkPreviewPolicies.MaxRedirects)
                {
                    return (LinkPreviewStatus.Failed, null, "TooManyRedirects");
                }

                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (LinkPreviewPolicies.NormalizeUrl(next.AbsoluteUri) is null)
                {
                    return (LinkPreviewStatus.Blocked, null, "RedirectDisallowedScheme");
                }

                current = next;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return (LinkPreviewStatus.Failed, null, $"Http{(int)response.StatusCode}");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (mediaType.Length > 0
                && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                && !mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                return (LinkPreviewStatus.Failed, null, "UnsupportedContentType");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var limited = await ReadLimitedAsync(stream, LinkPreviewPolicies.MaxHtmlBodyBytes, cancellationToken);
            var html = Encoding.UTF8.GetString(limited);
            return (LinkPreviewStatus.Ready, html, null);
        }

        return (LinkPreviewStatus.Failed, null, "TooManyRedirects");
    }

    private async Task<LinkPreviewFetchResult> FetchImageAsync(Uri startUri, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var current = startUri;
        for (var redirect = 0; redirect <= LinkPreviewPolicies.MaxRedirects; redirect++)
        {
            if (!await EnsureHostSafeAsync(current.Host, cancellationToken))
            {
                return Blocked("PrivateOrMetadataIp");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("Accept", "image/*,*/*;q=0.8");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location is null || redirect == LinkPreviewPolicies.MaxRedirects)
                {
                    return Failed("TooManyRedirects");
                }

                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (LinkPreviewPolicies.NormalizeUrl(next.AbsoluteUri) is null)
                {
                    return Blocked("RedirectDisallowedScheme");
                }

                current = next;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failed($"Http{(int)response.StatusCode}");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var bytes = await ReadLimitedAsync(stream, LinkPreviewPolicies.MaxImageBodyBytes, cancellationToken);
            if (bytes.Length == 0)
            {
                return Failed("EmptyImage");
            }

            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                && !LooksLikeImage(bytes))
            {
                return Failed("NotAnImage");
            }

            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                contentType = GuessImageContentType(bytes) ?? "image/jpeg";
            }

            return new LinkPreviewFetchResult(LinkPreviewStatus.Ready, null, null, null, bytes, contentType, null);
        }

        return Failed("TooManyRedirects");
    }

    private static Uri? ResolveImageUri(Uri pageUri, string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            trimmed = $"{pageUri.Scheme}:{trimmed}";
        }

        if (LinkPreviewPolicies.NormalizeUrl(trimmed) is { } absolute)
        {
            return new Uri(absolute, UriKind.Absolute);
        }

        if (Uri.TryCreate(pageUri, trimmed, out var relative)
            && LinkPreviewPolicies.NormalizeUrl(relative.AbsoluteUri) is { } normalized)
        {
            return new Uri(normalized, UriKind.Absolute);
        }

        return null;
    }

    private static bool LooksLikeImage(byte[] bytes)
    {
        if (bytes.Length < 4)
        {
            return false;
        }

        // JPEG / PNG / GIF / WEBP (RIFF....WEBP)
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return true;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true;
        if (bytes.Length >= 12
            && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return true;
        }

        return false;
    }

    private static string? GuessImageContentType(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8) return "image/jpeg";
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return "image/gif";
        if (bytes.Length >= 12 && bytes[8] == 0x57 && bytes[9] == 0x45) return "image/webp";
        return null;
    }

    private static async Task<bool> EnsureHostSafeAsync(string host, CancellationToken cancellationToken)
    {
        if (LinkPreviewPolicies.TryParseHostAsIp(host, out var literal)
            && LinkPreviewPolicies.IsBlockedIp(literal))
        {
            return false;
        }

        var addresses = await ResolveSafeAddressesAsync(host, cancellationToken);
        return addresses.Length > 0;
    }

    private static async Task<IPAddress[]> ResolveSafeAddressesAsync(string host, CancellationToken cancellationToken)
    {
        if (LinkPreviewPolicies.TryParseHostAsIp(host, out var literal))
        {
            return LinkPreviewPolicies.IsBlockedIp(literal) ? [] : [literal];
        }

        var all = await Dns.GetHostAddressesAsync(host, cancellationToken);
        return all.Where(a => !LinkPreviewPolicies.IsBlockedIp(a)).ToArray();
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[8192];
        var total = 0;
        while (total < maxBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxBytes - total)), cancellationToken);
            if (read == 0)
            {
                break;
            }

            ms.Write(buffer, 0, read);
            total += read;
        }

        return ms.ToArray();
    }

    private static LinkPreviewFetchResult Blocked(string error) =>
        new(LinkPreviewStatus.Blocked, null, null, null, null, null, error);

    private static LinkPreviewFetchResult Failed(string error) =>
        new(LinkPreviewStatus.Failed, null, null, null, null, null, error);

    private static string TruncateError(string message) =>
        message.Length <= 200 ? message : message[..200];
}

public static class OpenGraphParser
{
    private static readonly Regex MetaPropertyRegex = new(
        @"<meta\s+[^>]*(?:property|name)\s*=\s*[""'](?<prop>(?:og|twitter):[^""']+)[""'][^>]*content\s*=\s*[""'](?<content>[^""']*)[""'][^>]*/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MetaPropertyReverseRegex = new(
        @"<meta\s+[^>]*content\s*=\s*[""'](?<content>[^""']*)[""'][^>]*(?:property|name)\s*=\s*[""'](?<prop>(?:og|twitter):[^""']+)[""'][^>]*/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TitleRegex = new(
        @"<title[^>]*>(?<title>.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline);

    public sealed record ParsedMeta(string? Title, string? Description, string? SiteName, string? ImageUrl);

    public static ParsedMeta Parse(string html, Uri pageUri)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MetaPropertyRegex.Matches(html))
        {
            map[match.Groups["prop"].Value] = Decode(match.Groups["content"].Value);
        }

        foreach (Match match in MetaPropertyReverseRegex.Matches(html))
        {
            map.TryAdd(match.Groups["prop"].Value, Decode(match.Groups["content"].Value));
        }

        map.TryGetValue("og:title", out var title);
        map.TryGetValue("og:description", out var description);
        map.TryGetValue("og:site_name", out var siteName);
        map.TryGetValue("og:image", out var image);
        if (string.IsNullOrWhiteSpace(image))
        {
            map.TryGetValue("og:image:url", out image);
        }

        if (string.IsNullOrWhiteSpace(image))
        {
            map.TryGetValue("twitter:image", out image);
        }

        if (string.IsNullOrWhiteSpace(image))
        {
            map.TryGetValue("twitter:image:src", out image);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            map.TryGetValue("twitter:title", out title);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            var titleMatch = TitleRegex.Match(html);
            if (titleMatch.Success)
            {
                title = Decode(titleMatch.Groups["title"].Value);
            }
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            map.TryGetValue("twitter:description", out description);
        }

        if (!string.IsNullOrWhiteSpace(image))
        {
            var raw = image.Trim();
            if (raw.StartsWith("//", StringComparison.Ordinal))
            {
                raw = $"{pageUri.Scheme}:{raw}";
            }

            if (Uri.TryCreate(pageUri, raw, out var absoluteImage))
            {
                image = absoluteImage.AbsoluteUri;
            }
        }

        if (string.IsNullOrWhiteSpace(siteName))
        {
            siteName = pageUri.Host;
        }

        return new ParsedMeta(
            string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            string.IsNullOrWhiteSpace(siteName) ? null : siteName.Trim(),
            string.IsNullOrWhiteSpace(image) ? null : image.Trim());
    }

    private static string Decode(string value) =>
        System.Net.WebUtility.HtmlDecode(value ?? string.Empty).Trim();
}

/// <summary>Pins TCP connect to public DNS answers so redirects/DNS rebinding cannot hit private IPs mid-request.</summary>
public static class LinkPreviewHttpHandlerFactory
{
    public static SocketsHttpHandler Create()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var host = context.DnsEndPoint.Host;
                IPAddress[] addresses;
                if (LinkPreviewPolicies.TryParseHostAsIp(host, out var literal))
                {
                    addresses = [literal];
                }
                else
                {
                    addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                }

                foreach (var address in addresses)
                {
                    if (LinkPreviewPolicies.IsBlockedIp(address))
                    {
                        continue;
                    }

                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                    }
                }

                throw new HttpRequestException($"No safe public address for host '{host}'.");
            }
        };
    }
}
