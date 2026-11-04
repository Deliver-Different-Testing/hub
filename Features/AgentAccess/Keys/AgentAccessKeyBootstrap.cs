using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.SimpleSystemsManagement.Model;
using Hub.Features.AgentAccess.Oauth;
using Hub.Services;

namespace Hub.Features.AgentAccess.Keys;

/// <param name="SsmPrefix">Per-tenant SSM path, from env AgentAccessSsmPrefix. Unset = /hub/&lt;env&gt;/&lt;tenant&gt;/agent-access, both taken from the issuer host.</param>
/// <param name="Issuer">https origin of this tenant Hub, from env AgentAccessIssuer, else derived from TenantURL (see ResolveIssuer).</param>
/// <param name="KeySource">Where the key parameters are read from and created in.</param>
/// <param name="LoadTimeout">Upper bound on the whole startup SSM exchange: read, any create and the re-read (default 10 s).</param>
/// <param name="Clients">Pre-registered OAuth clients (AgentAccess:Clients); seeded once agent access opens.</param>
/// <param name="ClientMetadataHosts">Hosts whose client ID metadata documents may be fetched (AgentAccess:ClientMetadata:AllowedHosts). Empty = the feature is off.</param>
/// <param name="McpClientSecret">Secret of the confidential token-exchange client dfrnt-mcp, one per tenant Hub, from env AgentAccessMcpClientSecret. Unset = the exchange is off.</param>
/// <param name="McpResource">Optional override of this tenant's MCP resource URL, from env AgentAccessMcpResource. Unset = derived from the issuer.</param>
/// <param name="KeyWriteSettle">Pause after creating keys before reading them back (default 2 s), so pods starting together see each other's keys.</param>
/// <param name="ClusterIssuer">OIDC issuer of the Kubernetes cluster whose projected service-account tokens the MCP server presents (AgentAccess:ClusterIssuer). Unset = assertion auth is off.</param>
/// <param name="McpServiceAccounts">Service-account subjects (system:serviceaccount:ns:sa, or a prefix of one ending in *) allowed to authenticate as dfrnt-mcp (AgentAccess:McpServiceAccounts). Empty = assertion auth is off.</param>
public sealed record AgentAccessOAuthSettings(
    string? SsmPrefix = null,
    string? Issuer = null,
    IAgentAccessKeySource? KeySource = null,
    TimeSpan? LoadTimeout = null,
    IReadOnlyList<AgentAccessClientOptions>? Clients = null,
    string? McpResource = null,
    IReadOnlyList<string>? ClientMetadataHosts = null,
    string? McpClientSecret = null,
    TimeSpan? KeyWriteSettle = null,
    string? ClusterIssuer = null,
    IReadOnlyList<string>? McpServiceAccounts = null);

public enum AgentAccessIssuerSource { None, Configured, TenantUrl }

public sealed record AgentAccessBootstrapResult(AgentAccessKeys? Keys, Uri? Issuer, AgentAccessKeyState State);

/// <summary>
/// Startup decision: persistent keys from SSM plus a valid issuer, or not. A key missing from SSM is
/// created there and read back, so every pod signs with keys the others can read. Never throws; any
/// problem yields "not persistent" with a reason that contains no key material or parameter path, and
/// Hub carries on with ephemeral keys while the agent access gate stays closed.
/// </summary>
public static class AgentAccessKeyBootstrap
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultWriteSettle = TimeSpan.FromSeconds(2);
    private const int MaxWriteAttempts = 3;
    private const int NewKeySize = 3072;

    public static AgentAccessBootstrapResult Resolve(AgentAccessOAuthSettings? settings)
    {
        var issuer = ParseIssuer(settings?.Issuer, out var issuerFailure);
        var prefix = ResolvePrefix(settings?.SsmPrefix, issuer);

        if (settings is null || prefix is null)
        {
            return NotPersistent(null, issuer, "AgentAccessSsmPrefix is not set and AgentAccessIssuer names no tenant; using ephemeral keys");
        }

        if (settings.KeySource is null)
        {
            return NotPersistent(null, issuer, "no SSM key source is configured; using ephemeral keys");
        }

        (AgentAccessKeyLoadResult Load, bool Created) outcome;
        try
        {
            using var timeout = new CancellationTokenSource(settings.LoadTimeout ?? DefaultTimeout);
            outcome = LoadOrCreateAsync(settings.KeySource, prefix, settings.KeyWriteSettle ?? DefaultWriteSettle, timeout.Token)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return NotPersistent(null, issuer, "reading or creating the keys in SSM timed out; using ephemeral keys");
        }
        catch (Exception ex)
        {
            // Type only: the message can carry the parameter path or account details.
            return NotPersistent(null, issuer, $"reading or creating the keys in SSM failed ({ex.GetType().Name}); using ephemeral keys");
        }

        var load = outcome.Load;
        if (load.Keys is null)
        {
            return NotPersistent(null, issuer, $"the SSM keys are not usable: {load.Failure}; using ephemeral keys");
        }

        if (issuer is null)
        {
            return NotPersistent(load.Keys, null, issuerFailure);
        }

        var reason = outcome.Created ? "persistent keys created in SSM and loaded back" : "persistent keys loaded from SSM";
        return new AgentAccessBootstrapResult(
            load.Keys, issuer, new AgentAccessKeyState(true, reason, load.Keys.Signing.KeyId, load.Keys.Encryption.KeyId));
    }

    private const string TenantUrlPattern = "https://app_name.";

    /// <summary>
    /// AgentAccessIssuer when set, else this tenant's Hub origin from TenantURL (https://app_name.&lt;tenant host&gt;
    /// becomes https://hub.&lt;tenant host&gt;), but only when that names a deliverdifferent.com Hub with a known tenant.
    /// Otherwise none, and the gate stays closed.
    /// </summary>
    public static (string? Issuer, AgentAccessIssuerSource Source) ResolveIssuer(string? configuredIssuer, string? tenantUrl)
    {
        if (!string.IsNullOrWhiteSpace(configuredIssuer)) return (configuredIssuer.Trim(), AgentAccessIssuerSource.Configured);

        return HubOriginFromTenantUrl(tenantUrl) is { } derived
            ? (derived, AgentAccessIssuerSource.TenantUrl)
            : (null, AgentAccessIssuerSource.None);
    }

    private static string? HubOriginFromTenantUrl(string? tenantUrl)
    {
        var trimmed = tenantUrl?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith(TenantUrlPattern, StringComparison.OrdinalIgnoreCase)) return null;

        if (!Uri.TryCreate("https://hub." + trimmed[TenantUrlPattern.Length..], UriKind.Absolute, out var uri)
            || !uri.IsDefaultPort
            || !uri.Host.EndsWith(".deliverdifferent.com", StringComparison.OrdinalIgnoreCase)
            || AccountService.ExtractEnvAndTenantFromHost(uri.Host) is null)
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// The configured prefix, else /hub/&lt;env&gt;/&lt;tenant&gt;/agent-access (env prod or staging) for the issuer's
    /// host; null when there is neither. The env keeps staging and production apart if they share an AWS account.
    /// </summary>
    public static string? ResolvePrefix(string? configuredPrefix, string? issuer) =>
        ResolvePrefix(configuredPrefix, ParseIssuer(issuer, out _));

    private static string? ResolvePrefix(string? configuredPrefix, Uri? issuer)
    {
        if (!string.IsNullOrWhiteSpace(configuredPrefix)) return configuredPrefix.Trim();

        return issuer is not null && AccountService.ExtractEnvAndTenantFromHost(issuer.Host) is var (env, tenant)
            ? $"/hub/{env}/{tenant}/agent-access"
            : null;
    }

    // Keys are only ever used as SSM gives them back, never from memory: a key no other pod can read must not sign.
    private static async Task<(AgentAccessKeyLoadResult Load, bool Created)> LoadOrCreateAsync(
        IAgentAccessKeySource source, string prefix, TimeSpan settle, CancellationToken cancellationToken)
    {
        var load = AgentAccessKeyLoader.Load(await source.ReadAsync(prefix, cancellationToken));

        for (var attempt = 1; load.SigningMissing || load.EncryptionMissing; attempt++)
        {
            try
            {
                if (load.SigningMissing)
                {
                    await CreateKeyAsync(source, prefix, AgentAccessKeyLoader.SigningFolder, cancellationToken);
                }

                if (load.EncryptionMissing)
                {
                    await CreateKeyAsync(source, prefix, AgentAccessKeyLoader.EncryptionFolder, cancellationToken);
                }

                await Task.Delay(settle, cancellationToken);
                return (AgentAccessKeyLoader.Load(await source.ReadAsync(prefix, cancellationToken)), true);
            }
            catch (Exception ex) when (attempt < MaxWriteAttempts && IsContention(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(200, 801)), cancellationToken);
                load = AgentAccessKeyLoader.Load(await source.ReadAsync(prefix, cancellationToken));
            }
        }

        return (load, false);
    }

    private static Task CreateKeyAsync(IAgentAccessKeySource source, string prefix, string folder, CancellationToken cancellationToken)
    {
        using var rsa = RSA.Create(NewKeySize);
        var name = $"{folder}/{AgentAccessKeyLoader.NewKeyId(DateTime.UtcNow)}";
        return source.CreateAsync(prefix, name, rsa.ExportPkcs8PrivateKeyPem(), cancellationToken);
    }

    // Another pod writing at the same moment, or SSM pushing back; worth a re-read and another go.
    private static bool IsContention(Exception ex) =>
        ex is ParameterAlreadyExistsException or TooManyUpdatesException
        || ex is AmazonServiceException { ErrorCode: "ThrottlingException" };

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
