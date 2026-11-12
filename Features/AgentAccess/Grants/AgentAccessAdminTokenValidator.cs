using Microsoft.IdentityModel.JsonWebTokens;
using Serilog;
using Hub.Features.AgentAccess.Oauth;

namespace Hub.Features.AgentAccess.Grants;

/// <summary>The admin allow-list (AgentAccess:AdminServiceAccounts) on the same cluster issuer as the MCP server's.</summary>
public sealed record AgentAccessAdminTokenOptions(AgentAccessMcpAssertionOptions Options);

/// <summary>Authenticates the admin connections APIs' caller by its Kubernetes projected service-account token.</summary>
public interface IAgentAccessAdminTokenValidator
{
    /// <param name="authorization">The Authorization header as sent ("Bearer ...").</param>
    /// <returns>The service-account subject, or null when the caller is refused (always logged, never the token).</returns>
    Task<string?> AuthenticateAsync(string? authorization, CancellationToken cancellationToken = default);
}

public sealed class AgentAccessAdminTokenValidator : IAgentAccessAdminTokenValidator
{
    /// <summary>Fixed, because configurator's deploy values are shared across tenants and cannot name each tenant's Hub URL.</summary>
    public const string Audience = "dfrnt-hub-admin";

    private const string Scheme = "Bearer ";
    private readonly AgentAccessMcpAssertionValidator _validator;

    public AgentAccessAdminTokenValidator(AgentAccessAdminTokenOptions options, IAgentAccessClusterJwks jwks) =>
        _validator = new AgentAccessMcpAssertionValidator(options.Options, jwks);

    public async Task<string?> AuthenticateAsync(string? authorization, CancellationToken cancellationToken = default)
    {
        if (authorization is null || !authorization.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            || authorization[Scheme.Length..].Trim() is not { Length: > 0 } token)
        {
            Log.Warning("Agent admin API request rejected: no bearer token");
            return null;
        }

        var result = await _validator.ValidateAsync(token, [Audience], cancellationToken);
        if (result.Accepted) return result.ServiceAccount;

        Log.Warning("Agent admin API request rejected: {Reason} (unverified token subject {Subject})",
            result.Failure, UnverifiedSubject(token));
        return null;
    }

    // Only for the log, so a wrong namespace is easy to spot. Never trusted for any decision.
    private static string UnverifiedSubject(string token)
    {
        try
        {
            return SerilogAgentAdminAudit.Clean(new JsonWebTokenHandler().ReadJsonWebToken(token).Subject) is { Length: > 0 } s ? s : "(none)";
        }
        catch (Exception)
        {
            return "(unreadable)";
        }
    }
}
