using System.Text.Json;

namespace Hub.Features.AgentAccess.ClientMetadata;

// JwksUri is set for a private_key_jwt client (such as ChatGPT), which signs its token requests with keys published there.
public sealed record ClientMetadata(string ClientId, string ClientName, IReadOnlyList<string> RedirectUris, Uri? JwksUri = null);

public sealed record ClientMetadataParseResult(ClientMetadata? Document, string? Error)
{
    public static ClientMetadataParseResult Fail(string error) => new(null, error);
}

// A client ID metadata document is untrusted input from the internet (phase 1.8). Only a small, strict subset is
// accepted: a client using the authorization code flow, identified by the exact URL it was fetched from, that is
// either public or authenticates with private_key_jwt against a published key set (never a shared secret).
// Redirects are https, or http on a loopback address for native apps such as Claude Code (RFC 8252).
public static class ClientMetadataDocument
{
    private const int MaxClientIdLength = 100; // McpOAuthApplication.ClientId is nvarchar(100)
    private const int MaxRedirectUris = 10;
    private const int MaxRedirectUriLength = 300;
    private const int MaxNameLength = 100;

    private static readonly string[] ForbiddenFields =
        ["client_secret", "client_secret_expires_at", "jwks", "tls_client_auth_subject_dn"];

    // OpenIddict validates client assertions with these; HS* would need a shared secret.
    private static readonly string[] SigningAlgorithms = ["RS256", "ES256"];

    // ChatGPT lists the jwt-bearer grant. It is tolerated in the document but never granted: the application row only
    // ever gets the authorization code and refresh token permissions.
    private const string JwtBearerGrant = "urn:ietf:params:oauth:grant-type:jwt-bearer";

    public static bool IsAcceptableClientIdUrl(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrEmpty(value) || value.Length > MaxClientIdLength) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return false;

        if (parsed.Scheme != Uri.UriSchemeHttps
            || !parsed.IsDefaultPort
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Fragment)
            || !string.IsNullOrEmpty(parsed.Query)
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

            var method = "none";
            if (root.TryGetProperty("token_endpoint_auth_method", out var methodElement))
            {
                method = methodElement.ValueKind == JsonValueKind.String ? methodElement.GetString()! : string.Empty;
            }

            if (method is not ("none" or "private_key_jwt"))
            {
                return ClientMetadataParseResult.Fail("Only public or private_key_jwt clients are accepted.");
            }

            Uri? jwksUri = null;
            var hasJwksUri = root.TryGetProperty("jwks_uri", out var jwksElement);
            if (method == "none" && hasJwksUri) return ClientMetadataParseResult.Fail("jwks_uri is only allowed for private_key_jwt clients.");
            if (method == "private_key_jwt")
            {
                if (!hasJwksUri
                    || jwksElement.ValueKind != JsonValueKind.String
                    || !Uri.TryCreate(jwksElement.GetString(), UriKind.Absolute, out jwksUri)
                    || jwksUri.Scheme != Uri.UriSchemeHttps
                    || !string.IsNullOrEmpty(jwksUri.Fragment))
                {
                    return ClientMetadataParseResult.Fail("A private_key_jwt client needs an https jwks_uri.");
                }

                if (root.TryGetProperty("token_endpoint_auth_signing_alg", out var alg)
                    && !(alg.ValueKind == JsonValueKind.String && SigningAlgorithms.Contains(alg.GetString())))
                {
                    return ClientMetadataParseResult.Fail("Unsupported token_endpoint_auth_signing_alg.");
                }
            }

            if (!OnlyAllowed(root, "grant_types", "authorization_code", "refresh_token", JwtBearerGrant)) return ClientMetadataParseResult.Fail("Unsupported grant_types.");
            if (!OnlyAllowed(root, "response_types", "code")) return ClientMetadataParseResult.Fail("Unsupported response_types.");

            if (!TryString(root, "client_name", out var rawName) || Clean(rawName) is not { Length: > 0 } name)
            {
                return ClientMetadataParseResult.Fail("client_name is required.");
            }

            if (!TryRedirects(root, out var redirects, out var redirectError)) return ClientMetadataParseResult.Fail(redirectError!);

            return new ClientMetadataParseResult(new ClientMetadata(clientId!, name, redirects, jwksUri), null);
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
                || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                error = "Every redirect URI must be an absolute https (or loopback http) URI without a fragment.";
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
