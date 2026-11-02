namespace Hub.Features.AiAssistant;

public sealed class CspKnowledgeOptions
{
    public const string SectionName = "CspKnowledge";
    private const string AppPlaceholder = "app_name";
    private const string CspAppName = "dfrntcrm";

    public string? BaseUrl { get; set; }

    /// <summary>
    /// The configured base URL if there is one, otherwise the tenant app URL pattern pointed at CSP.
    /// Null means the knowledge base is off.
    /// </summary>
    public static string? Resolve(string? configured, string? tenantUrl)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return !string.IsNullOrWhiteSpace(tenantUrl) && tenantUrl.Contains(AppPlaceholder, StringComparison.Ordinal)
            ? tenantUrl.Trim().Replace(AppPlaceholder, CspAppName)
            : null;
    }

    /// <summary>Null when valid. The service token goes to this URL, so it must be https outside Development.</summary>
    public static string? Validate(string? baseUrl, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return "CspKnowledge:BaseUrl must be an absolute URL.";
        }

        if (uri.Scheme == Uri.UriSchemeHttps || (isDevelopment && uri.Scheme == Uri.UriSchemeHttp))
        {
            return null;
        }

        return isDevelopment
            ? "CspKnowledge:BaseUrl must be an absolute http:// or https:// URL."
            : "CspKnowledge:BaseUrl must be an absolute https:// URL outside Development.";
    }
}
