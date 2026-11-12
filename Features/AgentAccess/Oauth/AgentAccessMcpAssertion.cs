using System.Text.RegularExpressions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace Hub.Features.AgentAccess.Oauth;

/// <summary>
/// Which Kubernetes service accounts may authenticate as the token-exchange client dfrnt-mcp with their projected
/// service-account token (AgentAccess:ClusterIssuer and AgentAccess:McpServiceAccounts). Cluster-wide, never per tenant.
/// </summary>
public sealed partial class AgentAccessMcpAssertionOptions
{
    private AgentAccessMcpAssertionOptions(Uri? clusterIssuer, IReadOnlySet<string> serviceAccounts)
    {
        ClusterIssuer = clusterIssuer;
        ServiceAccounts = serviceAccounts;
    }

    /// <summary>The cluster's OIDC issuer (an https URL), or null when unset or unusable.</summary>
    public Uri? ClusterIssuer { get; }

    /// <summary>
    /// Allowed subjects, each "system:serviceaccount:{namespace}:{service account}", or a prefix of one ending in a
    /// single "*" that matches every subject starting with the rest.
    /// </summary>
    public IReadOnlySet<string> ServiceAccounts { get; }

    /// <summary>False unless both the issuer and at least one service account are configured.</summary>
    public bool Enabled => ClusterIssuer is not null && ServiceAccounts.Count > 0;

    /// <summary>Ordinal: an exact entry, or a trailing-"*" entry whose prefix the subject starts with.</summary>
    public bool Allows(string subject) =>
        !string.IsNullOrEmpty(subject)
        && ServiceAccounts.Any(entry => entry.EndsWith('*')
            ? subject.StartsWith(entry[..^1], StringComparison.Ordinal)
            : string.Equals(entry, subject, StringComparison.Ordinal));

    public static AgentAccessMcpAssertionOptions Disabled { get; } = new(null, new HashSet<string>());

    [GeneratedRegex("^system:serviceaccount:[^:*]+:[^:*]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceAccountSubject();

    // At least the start of a namespace before the one "*": a broader pattern would let any workload in the cluster in.
    [GeneratedRegex(@"^system:serviceaccount:[^:*]+(:[^:*]*)?\*$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceAccountPrefix();

    public static AgentAccessMcpAssertionOptions Resolve(
        string? clusterIssuer, IEnumerable<string>? serviceAccounts, string allowListSetting = "AgentAccess:McpServiceAccounts")
    {
        Uri? issuer = null;
        if (!string.IsNullOrWhiteSpace(clusterIssuer))
        {
            if (Uri.TryCreate(clusterIssuer.Trim(), UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && string.IsNullOrEmpty(uri.Query)
                && string.IsNullOrEmpty(uri.Fragment))
            {
                issuer = uri;
            }
            else
            {
                Log.Warning("AgentAccess:ClusterIssuer must be an https URL; service-account token authentication stays off");
            }
        }

        var accounts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in serviceAccounts ?? [])
        {
            var trimmed = account?.Trim() ?? string.Empty;
            if (ServiceAccountSubject().IsMatch(trimmed) || ServiceAccountPrefix().IsMatch(trimmed))
            {
                accounts.Add(trimmed);
            }
            else
            {
                Log.Warning("{Setting} entry {Entry} is not system:serviceaccount:<namespace>:<name> or a prefix of one ending in a single *; it is ignored", allowListSetting, account);
            }
        }

        return new AgentAccessMcpAssertionOptions(issuer, accounts);
    }
}

/// <summary>Where the cluster's signing keys come from: its OIDC discovery document.</summary>
public interface IAgentAccessClusterJwks
{
    BaseConfigurationManager? Manager { get; }
}

public sealed class AgentAccessClusterJwks : IAgentAccessClusterJwks
{
    public const string HttpClientName = "agent-access-cluster-jwks";

    private readonly Lazy<BaseConfigurationManager?> _manager;

    public AgentAccessClusterJwks(AgentAccessMcpAssertionOptions options, IHttpClientFactory httpClients)
    {
        _manager = new Lazy<BaseConfigurationManager?>(() =>
        {
            if (!options.Enabled) return null;

            var address = options.ClusterIssuer!.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration";
            return new ConfigurationManager<OpenIdConnectConfiguration>(
                address,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(httpClients.CreateClient(HttpClientName)) { RequireHttps = true })
            {
                // A token signed by a rotated key triggers a refresh, but never more than once a minute.
                RefreshInterval = TimeSpan.FromMinutes(1),
                AutomaticRefreshInterval = TimeSpan.FromHours(1)
            };
        });
    }

    public BaseConfigurationManager? Manager => _manager.Value;
}

/// <param name="ServiceAccount">The accepted service-account subject; null when refused.</param>
/// <param name="ExpiresAt">When the assertion itself expires.</param>
/// <param name="Failure">A short reason safe to log (never token contents); null when accepted.</param>
public sealed record AgentAccessMcpAssertionResult(string? ServiceAccount, DateTimeOffset? ExpiresAt, string? Failure)
{
    public bool Accepted => Failure is null && ServiceAccount is not null;

    public static AgentAccessMcpAssertionResult Refused(string failure) => new(null, null, failure);
}

public interface IAgentAccessMcpAssertionValidator
{
    bool Enabled { get; }

    /// <param name="assertion">The client_assertion as sent.</param>
    /// <param name="audiences">This Hub's issuer, in every accepted spelling.</param>
    Task<AgentAccessMcpAssertionResult> ValidateAsync(
        string assertion, IReadOnlyCollection<string> audiences, CancellationToken cancellationToken = default);
}

/// <summary>
/// A Kubernetes projected service-account token as the MCP server's proof of identity, checked against the cluster's
/// OIDC keys. Fails closed on every doubt; the reason it returns is for the log only and never reaches the caller.
/// </summary>
public sealed class AgentAccessMcpAssertionValidator(AgentAccessMcpAssertionOptions options, IAgentAccessClusterJwks jwks)
    : IAgentAccessMcpAssertionValidator
{
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public bool Enabled => options.Enabled;

    public async Task<AgentAccessMcpAssertionResult> ValidateAsync(
        string assertion, IReadOnlyCollection<string> audiences, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await ValidateCoreAsync(assertion, audiences);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        // A key fetch that times out surfaces as a cancellation too; only the caller's own cancellation propagates.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Log.Warning(ex, "Agent access could not validate the MCP server's service-account token");
            return AgentAccessMcpAssertionResult.Refused("validation_error");
        }
    }

    private async Task<AgentAccessMcpAssertionResult> ValidateCoreAsync(string assertion, IReadOnlyCollection<string> audiences)
    {
        if (!options.Enabled || jwks.Manager is not { } manager)
        {
            return AgentAccessMcpAssertionResult.Refused("not_configured");
        }

        var handler = new JsonWebTokenHandler();
        if (!handler.CanReadToken(assertion))
        {
            return AgentAccessMcpAssertionResult.Refused("not_a_jwt");
        }

        var configuredIssuer = options.ClusterIssuer!.OriginalString.Trim();
        var result = await handler.ValidateTokenAsync(assertion, new TokenValidationParameters
        {
            ConfigurationManager = manager,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuer = true,
            ValidIssuer = configuredIssuer,
            ValidateAudience = true,
            ValidAudiences = audiences,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew
        });
        if (!result.IsValid)
        {
            return AgentAccessMcpAssertionResult.Refused(Reason(result.Exception));
        }

        var token = (JsonWebToken)result.SecurityToken;

        // The handler also trusts the issuer named by the discovery document; pin it to the configured one.
        if (!string.Equals(token.Issuer, configuredIssuer, StringComparison.Ordinal))
        {
            return AgentAccessMcpAssertionResult.Refused("wrong_issuer");
        }

        if (!string.Equals(token.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
        {
            return AgentAccessMcpAssertionResult.Refused("bad_algorithm");
        }

        if (!token.TryGetPayloadValue<long>("iat", out var iat))
        {
            return AgentAccessMcpAssertionResult.Refused("no_issued_at");
        }

        var expires = new DateTimeOffset(token.ValidTo, TimeSpan.Zero);
        if (expires - DateTimeOffset.FromUnixTimeSeconds(iat) > MaxLifetime)
        {
            return AgentAccessMcpAssertionResult.Refused("lifetime_too_long");
        }

        if (token.Subject is not { Length: > 0 } subject || !options.Allows(subject))
        {
            return AgentAccessMcpAssertionResult.Refused("service_account_not_allowed");
        }

        return new AgentAccessMcpAssertionResult(subject, expires, null);
    }

    private static string Reason(Exception? exception) => exception switch
    {
        SecurityTokenInvalidIssuerException => "wrong_issuer",
        SecurityTokenInvalidAudienceException => "wrong_audience",
        SecurityTokenExpiredException => "expired",
        SecurityTokenNotYetValidException => "not_yet_valid",
        SecurityTokenNoExpirationException => "no_expiration",
        SecurityTokenInvalidAlgorithmException => "bad_algorithm",
        SecurityTokenSignatureKeyNotFoundException => "unknown_key",
        SecurityTokenInvalidSignatureException => "bad_signature",
        _ => "invalid_token"
    };
}

/// <summary>Whether the MCP server's token exchange can authenticate at all on this Hub: by secret, by service-account token, or both.</summary>
public sealed record AgentAccessMcpExchange(bool SecretConfigured, bool AssertionConfigured)
{
    public bool Configured => SecretConfigured || AssertionConfigured;
}
