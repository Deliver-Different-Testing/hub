using Hub.Features.AgentAccess.Oauth;

namespace Hub.Features.AgentAccess.Grants;

/// <summary>The capabilities allow-list (AgentAccess:CapabilityServiceAccounts) on the same cluster issuer as the MCP server's.</summary>
public sealed record AgentAccessCapabilitiesTokenOptions(AgentAccessMcpAssertionOptions Options);

/// <summary>Authenticates the apps that ask for a user's agent capabilities by their Kubernetes projected service-account token.</summary>
public interface IAgentAccessCapabilitiesTokenValidator
{
    /// <param name="authorization">The Authorization header as sent ("Bearer ...").</param>
    /// <returns>The service-account subject, or null when the caller is refused (always logged, never the token).</returns>
    Task<string?> AuthenticateAsync(string? authorization, CancellationToken cancellationToken = default);
}

public sealed class AgentAccessCapabilitiesTokenValidator(AgentAccessCapabilitiesTokenOptions options, IAgentAccessClusterJwks jwks)
    : IAgentAccessCapabilitiesTokenValidator
{
    /// <summary>Fixed for the same reason as the admin audience; distinct from it so neither token opens the other API.</summary>
    public const string Audience = "dfrnt-hub-agent-access";

    private readonly AgentAccessServiceTokenValidator _validator = new(options.Options, jwks, Audience, "Agent capabilities API");

    public Task<string?> AuthenticateAsync(string? authorization, CancellationToken cancellationToken = default) =>
        _validator.AuthenticateAsync(authorization, cancellationToken);
}
