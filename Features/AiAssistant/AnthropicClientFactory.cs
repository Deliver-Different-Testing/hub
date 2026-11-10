using Anthropic;
using Anthropic.Core;
using Anthropic.Credentials;

namespace Hub.Features.AiAssistant;

public static class AnthropicClientFactory
{
    /// <summary>
    /// Claude is reached with workload identity federation: the pod's projected service-account token is exchanged for a
    /// short-lived Anthropic token. Without a federation there is no key to fall back to, so calls fail as not connected.
    /// </summary>
    public static AnthropicClient Create(AiAssistantOptions options, HttpClient? httpClient = null)
    {
        var federation = options.Federation;
        var clientOptions = new ClientOptions
        {
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            MaxRetries = options.MaxRetries
        };
        if (httpClient is not null)
        {
            clientOptions.HttpClient = httpClient;
        }

        if (!federation.IsConfigured)
        {
            Serilog.Log.Warning("Anthropic workload identity federation is not configured; Auto-mate will report it is not connected");
            clientOptions.ApiKey = string.Empty;
            return new AnthropicClient(clientOptions);
        }

        Serilog.Log.Information("Anthropic client authenticating via workload identity federation (rule {RuleId})", federation.RuleId);
        clientOptions.Credentials = new WorkloadIdentityCredentials(new WorkloadIdentityOptions
        {
            FederationRuleId = federation.RuleId!,
            OrganizationId = federation.OrganizationId,
            ServiceAccountId = federation.ServiceAccountId,
            WorkspaceId = string.IsNullOrWhiteSpace(federation.WorkspaceId) ? null : federation.WorkspaceId,
            IdentityTokenProvider = new FileIdentityTokenProvider(federation.IdentityTokenFile),
            HttpClient = httpClient
        });
        return new AnthropicClient(clientOptions);
    }
}
