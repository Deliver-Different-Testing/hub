using Hub.Interfaces;
using Hub.Services;

namespace Hub.Features.AgentAccess;

/// <param name="SsmPrefix">Per-tenant SSM path, from env AgentAccessSsmPrefix. Unset = ephemeral keys.</param>
/// <param name="Issuer">https origin of this tenant Hub, from env AgentAccessIssuer.</param>
/// <param name="KeySource">Where the key parameters are read from.</param>
/// <param name="LoadTimeout">Upper bound on the startup SSM read (default 10 s).</param>
/// <param name="Clients">Pre-registered OAuth clients (AgentAccess:Clients); seeded once agent access opens.</param>
/// <param name="ClientMetadataHosts">Hosts whose client ID metadata documents may be fetched (AgentAccess:ClientMetadata:AllowedHosts). Empty = the feature is off.</param>
/// <param name="McpResource">Optional override of this tenant's MCP resource URL, from env AgentAccessMcpResource. Unset = derived from the issuer.</param>
public sealed record AgentAccessOAuthSettings(
    string? SsmPrefix = null,
    string? Issuer = null,
    IAgentAccessKeySource? KeySource = null,
    TimeSpan? LoadTimeout = null,
    IReadOnlyList<AgentAccessClientOptions>? Clients = null,
    string? McpResource = null,
    IReadOnlyList<string>? ClientMetadataHosts = null);

public sealed record AgentAccessBootstrapResult(AgentAccessKeys? Keys, Uri? Issuer, AgentAccessKeyState State);

/// <summary>
/// Startup decision: persistent keys from SSM plus a valid issuer, or not. Never throws; any
/// problem yields "not persistent" with a reason that contains no key material, and Hub
/// carries on with ephemeral keys while the agent access gate stays closed.
/// </summary>
public static class AgentAccessKeyBootstrap
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static AgentAccessBootstrapResult Resolve(AgentAccessOAuthSettings? settings)
    {
        var issuer = ParseIssuer(settings?.Issuer, out var issuerFailure);

        if (settings is null || string.IsNullOrWhiteSpace(settings.SsmPrefix))
        {
            return NotPersistent(null, issuer, "AgentAccessSsmPrefix is not set; using ephemeral keys");
        }

        if (settings.KeySource is null)
        {
            return NotPersistent(null, issuer, "no SSM key source is configured; using ephemeral keys");
        }

        IReadOnlyDictionary<string, string> parameters;
        try
        {
            using var timeout = new CancellationTokenSource(settings.LoadTimeout ?? DefaultTimeout);
            parameters = settings.KeySource.ReadAsync(settings.SsmPrefix, timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return NotPersistent(null, issuer, "reading the keys from SSM timed out; using ephemeral keys");
        }
        catch (Exception ex)
        {
            // Type only: the message can carry the parameter path or account details.
            return NotPersistent(null, issuer, $"reading the keys from SSM failed ({ex.GetType().Name}); using ephemeral keys");
        }

        var load = AgentAccessKeyLoader.Load(parameters);
        if (load.Keys is null)
        {
            return NotPersistent(null, issuer, $"the SSM keys are not usable: {load.Failure}; using ephemeral keys");
        }

        if (issuer is null)
        {
            return NotPersistent(load.Keys, null, issuerFailure);
        }

        return new AgentAccessBootstrapResult(
            load.Keys, issuer, new AgentAccessKeyState(true, "persistent keys loaded from SSM", load.Keys.Signing.KeyId));
    }

    private static AgentAccessBootstrapResult NotPersistent(AgentAccessKeys? keys, Uri? issuer, string reason) =>
        new(keys, issuer, new AgentAccessKeyState(false, reason, null));

    // An https origin with no path: the value tokens carry in iss and apps validate against.
    private static Uri? ParseIssuer(string? value, out string failure)
    {
        failure = "AgentAccessIssuer is not set";
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            failure = "AgentAccessIssuer must be an https origin with no path";
            return null;
        }

        failure = string.Empty;
        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }
}
