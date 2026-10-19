using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace Hub.Features.AgentAccess.ClientMetadata;

public sealed record ClientMetadataOptions(
    IReadOnlyList<string> AllowedHosts,
    TimeSpan Ttl,
    int MaxBytes,
    TimeSpan Timeout,
    TimeSpan NegativeTtl,
    TimeSpan RefetchFloor,
    TimeSpan MaxStale)
{
    public static ClientMetadataOptions Create(IEnumerable<string>? allowedHosts) => new(
        (allowedHosts ?? []).Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim().TrimStart('/').ToLowerInvariant()).Distinct().ToList(),
        Ttl: TimeSpan.FromHours(1),
        MaxBytes: 16 * 1024,
        Timeout: TimeSpan.FromSeconds(5),
        NegativeTtl: TimeSpan.FromMinutes(5),
        RefetchFloor: TimeSpan.FromMinutes(1),
        MaxStale: TimeSpan.FromHours(2));

    public bool Enabled => AllowedHosts.Count > 0;

    // A client's redirect URIs must live on a host this Hub trusts as much as the client document itself, so a
    // document on an allowed host can never send an authorization code to somewhere else.
    public bool IsTrustedRedirectHost(string host) => AllowedHosts.Any(entry =>
    {
        var slash = entry.IndexOf('/');
        return string.Equals(host, slash < 0 ? entry : entry[..slash], StringComparison.OrdinalIgnoreCase);
    });

    // An entry is a host, optionally followed by a path prefix ("claude.ai/oauth/"). A bare host allows every path
    // on it, so prefer a prefix: a host that serves user content must not let anyone register a client.
    public bool IsAllowed(Uri url) => AllowedHosts.Any(entry =>
    {
        var slash = entry.IndexOf('/');
        var host = slash < 0 ? entry : entry[..slash];
        var prefix = slash < 0 ? string.Empty : entry[slash..];
        return string.Equals(url.Host, host, StringComparison.OrdinalIgnoreCase)
               && url.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal);
    });
}

public sealed record ClientMetadataFetchResult(string? Body, string? Error);

public interface IClientMetadataFetcher
{
    Task<ClientMetadataFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken = default);
}

// Fetches a client ID metadata document (phase 1.8). The URL is chosen by whoever sends the authorization request,
// so: no redirects, no cookies, no credentials, a hard size cap, a short timeout, JSON only, and (via
// SsrfSafeConnect on the handler) public addresses only.
public sealed class ClientMetadataFetcher(HttpClient httpClient, ClientMetadataOptions options) : IClientMetadataFetcher
{
    public async Task<ClientMetadataFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return Fail($"HTTP {(int)response.StatusCode}");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !(mediaType == "application/json" || mediaType.EndsWith("+json", StringComparison.Ordinal)))
            {
                return Fail("The response is not JSON.");
            }

            if (response.Content.Headers.ContentLength > options.MaxBytes) return Fail("The document is too large.");

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[options.MaxBytes + 1];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read), timeout.Token);
                if (n == 0) break;
                read += n;
            }

            return read > options.MaxBytes ? Fail("The document is too large.") : new ClientMetadataFetchResult(Encoding.UTF8.GetString(buffer, 0, read), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Fail("The request timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            return Fail(ex.GetBaseException().Message);
        }
    }

    private static ClientMetadataFetchResult Fail(string error) => new(null, error);
}

// Connects only to public addresses, resolved and checked at connection time so a name that later resolves to an
// internal address (DNS rebinding) is still refused.
public static class SsrfSafeConnect
{
    public static async Task<IPAddress> ResolvePublicAddressAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var allowed = addresses.FirstOrDefault(SsrfGuard.IsPublic);
        if (allowed is null || addresses.Any(a => !SsrfGuard.IsPublic(a)))
        {
            throw new HttpRequestException("The host does not resolve to a public address.");
        }

        return allowed;
    }

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var address = await ResolvePublicAddressAsync(context.DnsEndPoint.Host, cancellationToken);
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

// Server-side request forgery guard (phase 1.8): the metadata URL is chosen by whoever sends the authorization
// request, so Hub must only ever connect to public internet addresses.
public static class SsrfGuard
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return false;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0
                     || b[0] == 10
                     || (b[0] == 100 && b[1] is >= 64 and <= 127)
                     || (b[0] == 169 && b[1] == 254)
                     || (b[0] == 172 && b[1] is >= 16 and <= 31)
                     || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 198 && b[1] is 18 or 19)
                     || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                     || (b[0] == 203 && b[1] == 0 && b[2] == 113)
                     || b[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.Equals(IPAddress.IPv6Any)
                     || address.IsIPv6LinkLocal
                     || address.IsIPv6SiteLocal
                     || address.IsIPv6Multicast
                     || (b[0] & 0xFE) == 0xFC);
        }

        return false;
    }
}
