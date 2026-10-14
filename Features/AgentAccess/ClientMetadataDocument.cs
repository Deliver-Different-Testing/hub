using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Hub.Features.AgentAccess;

public sealed record ClientMetadata(string ClientId, string ClientName, IReadOnlyList<string> RedirectUris);

public sealed record ClientMetadataParseResult(ClientMetadata? Document, string? Error)
{
    public static ClientMetadataParseResult Fail(string error) => new(null, error);
}

// A client ID metadata document is untrusted input from the internet (phase 1.8). Only a small, strict subset is
// accepted: a public client using the authorization code flow, identified by the exact URL it was fetched from.
public static class ClientMetadataDocument
{
    public const int MaxClientIdLength = 100; // McpOAuthApplication.ClientId is nvarchar(100)
    private const int MaxRedirectUris = 10;
    private const int MaxRedirectUriLength = 300;
    private const int MaxNameLength = 100;

    private static readonly string[] ForbiddenFields =
        ["client_secret", "client_secret_expires_at", "jwks", "jwks_uri", "tls_client_auth_subject_dn"];

    public static bool IsAcceptableClientIdUrl(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrEmpty(value) || value.Length > MaxClientIdLength) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return false;

        if (parsed.Scheme != Uri.UriSchemeHttps
            || !parsed.IsDefaultPort
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Fragment)
            || parsed.HostNameType != UriHostNameType.Dns
            || !parsed.Host.Contains('.')
            || parsed.AbsolutePath is "" or "/"
            || value.Contains("/../", StringComparison.Ordinal)
            || value.Contains("/./", StringComparison.Ordinal)
            || !string.Equals(parsed.OriginalString, value, StringComparison.Ordinal))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    public static ClientMetadataParseResult Parse(string body, string expectedClientId)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException)
        {
            return ClientMetadataParseResult.Fail("The document is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ClientMetadataParseResult.Fail("The document is not a JSON object.");

            if (!TryString(root, "client_id", out var clientId) || !string.Equals(clientId, expectedClientId, StringComparison.Ordinal))
            {
                return ClientMetadataParseResult.Fail("client_id does not equal the document URL.");
            }

            foreach (var field in ForbiddenFields)
            {
                if (root.TryGetProperty(field, out _)) return ClientMetadataParseResult.Fail($"{field} is not allowed.");
            }

            if (root.TryGetProperty("token_endpoint_auth_method", out var method)
                && !(method.ValueKind == JsonValueKind.String && method.GetString() == "none"))
            {
                return ClientMetadataParseResult.Fail("Only public clients (token_endpoint_auth_method none) are accepted.");
            }

            if (!OnlyAllowed(root, "grant_types", "authorization_code", "refresh_token")) return ClientMetadataParseResult.Fail("Unsupported grant_types.");
            if (!OnlyAllowed(root, "response_types", "code")) return ClientMetadataParseResult.Fail("Unsupported response_types.");

            if (!TryString(root, "client_name", out var rawName) || Clean(rawName) is not { Length: > 0 } name)
            {
                return ClientMetadataParseResult.Fail("client_name is required.");
            }

            if (!TryRedirects(root, out var redirects, out var redirectError)) return ClientMetadataParseResult.Fail(redirectError!);

            return new ClientMetadataParseResult(new ClientMetadata(clientId!, name, redirects), null);
        }
    }

    private static bool TryString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return false;

        value = element.GetString();
        return true;
    }

    private static bool OnlyAllowed(JsonElement root, string name, params string[] allowed)
    {
        if (!root.TryGetProperty(name, out var element)) return true;
        if (element.ValueKind != JsonValueKind.Array) return false;

        return element.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String && allowed.Contains(v.GetString()));
    }

    private static bool TryRedirects(JsonElement root, out List<string> redirects, out string? error)
    {
        redirects = [];
        error = null;

        if (!root.TryGetProperty("redirect_uris", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            error = "redirect_uris is required.";
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrEmpty(value)
                || value.Length > MaxRedirectUriLength
                || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                error = "Every redirect URI must be an absolute https URI without a fragment.";
                return false;
            }

            redirects.Add(value);
        }

        if (redirects.Count is 0 or > MaxRedirectUris)
        {
            error = $"redirect_uris must list between 1 and {MaxRedirectUris} URIs.";
            return false;
        }

        return true;
    }

    private static string Clean(string? value)
    {
        var cleaned = new string((value ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > MaxNameLength ? cleaned[..MaxNameLength] : cleaned;
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
