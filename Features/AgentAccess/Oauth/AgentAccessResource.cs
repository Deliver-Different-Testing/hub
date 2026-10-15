using Hub.Services;

namespace Hub.Features.AgentAccess;

/// <summary>The single resource URL this tenant Hub accepts on /connect/authorize; null when none can be determined.</summary>
public sealed record AgentAccessMcpResource(string? Value);

public static class AgentAccessResource
{
    public const string McpPath = "/mcp";

    public static string? Resolve(string? issuer, string? explicitResource)
    {
        if (!string.IsNullOrWhiteSpace(explicitResource))
        {
            return IsValidExplicit(explicitResource.Trim()) ? explicitResource.Trim() : null;
        }

        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || (uri.AbsolutePath != "/" && uri.AbsolutePath.Length > 0)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.Host.StartsWith("hub.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var builder = new UriBuilder(uri.Scheme, "mcp." + uri.Host["hub.".Length..].ToLowerInvariant(), uri.IsDefaultPort ? -1 : uri.Port, McpPath);
        return builder.Uri.GetLeftPart(UriPartial.Path);
    }

    private static bool IsValidExplicit(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);
}
