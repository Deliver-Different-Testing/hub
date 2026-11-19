using System.Diagnostics;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.SimpleSystemsManagement.Model;
using Hub.Features.AgentAccess.Oauth;
using Hub.Services;
using Serilog;

namespace Hub.Features.AgentAccess.Keys;

/// <param name="SsmPrefix">Per-tenant SSM path, from env AgentAccessSsmPrefix. Unset = /hub/&lt;env&gt;/&lt;tenant&gt;/agent-access, both taken from the issuer host.</param>
/// <param name="Issuer">https origin of this tenant Hub, from env AgentAccessIssuer, else derived from TenantURL (see ResolveIssuer).</param>
/// <param name="KeySource">Where the key parameters are read from and created in.</param>
/// <param name="LoadTimeout">Upper bound on one attempt's SSM calls: read, any create and the re-read (default 10 s). Generating a missing key does not count.</param>
/// <param name="LoadAttempts">Attempts at startup before a passing SSM failure is left to the background recovery (default 4).</param>
/// <param name="LoadRetryDelay">Base of the jittered exponential pause between startup attempts (default 1 s).</param>
/// <param name="RecoveryDelay">First pause of the background recovery after a passing startup failure (default 5 s).</param>
/// <param name="RecoveryMaxDelay">Longest pause of the background recovery (default 5 min).</param>
/// <param name="Clients">Pre-registered OAuth clients (AgentAccess:Clients); seeded once agent access opens.</param>
/// <param name="ClientMetadataHosts">Hosts whose client ID metadata documents may be fetched (AgentAccess:ClientMetadata:AllowedHosts). Empty = the feature is off.</param>
/// <param name="McpClientSecret">Secret of the confidential token-exchange client dfrnt-mcp, one per tenant Hub, from env AgentAccessMcpClientSecret. Unset = the exchange is off.</param>
/// <param name="McpResource">Optional override of this tenant's MCP resource URL, from env AgentAccessMcpResource. Unset = derived from the issuer.</param>
/// <param name="KeyWriteSettle">Pause after creating keys before reading them back (default 2 s), so pods starting together see each other's keys.</param>
/// <param name="ClusterIssuer">OIDC issuer of the Kubernetes cluster whose projected service-account tokens the MCP server presents (AgentAccess:ClusterIssuer). Unset = assertion auth is off.</param>
/// <param name="McpServiceAccounts">Service-account subjects (system:serviceaccount:ns:sa, or a prefix of one ending in *) allowed to authenticate as dfrnt-mcp (AgentAccess:McpServiceAccounts). Empty = assertion auth is off.</param>
/// <param name="AdminServiceAccounts">Service-account subjects allowed to call the admin connections APIs with their token (AgentAccess:AdminServiceAccounts). Empty = those APIs refuse everything.</param>
/// <param name="CapabilityServiceAccounts">Service-account subjects allowed to ask for a user's agent capabilities with their token (AgentAccess:CapabilityServiceAccounts). Empty = only the API key is accepted there.</param>
/// <param name="NewPrivateKeyPem">Makes the PKCS#8 PEM of a new key (default RSA 3072).</param>
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
    IReadOnlyList<string>? McpServiceAccounts = null,
    Func<string>? NewPrivateKeyPem = null,
    IReadOnlyList<string>? AdminServiceAccounts = null,
    int? LoadAttempts = null,
    TimeSpan? LoadRetryDelay = null,
    TimeSpan? RecoveryDelay = null,
    TimeSpan? RecoveryMaxDelay = null,
    IReadOnlyList<string>? CapabilityServiceAccounts = null);

public enum AgentAccessIssuerSource { None, Configured, TenantUrl }

public sealed record AgentAccessBootstrapResult(AgentAccessKeys? Keys, Uri? Issuer, AgentAccessKeyState State);

/// <summary>
/// Startup decision: persistent keys from SSM plus a valid issuer, or not. A key missing from SSM is
/// created there and read back, so every pod signs with keys the others can read. A transient SSM failure
/// is tried again a few times, then left to <see cref="AgentAccessKeyRecoveryService"/>. Never throws; any
/// problem yields "not persistent" with a reason that contains no key material or parameter path, and
/// Hub carries on with ephemeral keys while the agent access gate stays closed.
/// </summary>
public static class AgentAccessKeyBootstrap
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultWriteSettle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);
    private const int DefaultLoadAttempts = 4;
    private const int MaxWriteAttempts = 3;
    private const int NewKeySize = 3072;

    public static AgentAccessBootstrapResult Resolve(AgentAccessOAuthSettings? settings) =>
        ResolveAsync(settings, settings?.LoadAttempts ?? DefaultLoadAttempts, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Up to <paramref name="attempts"/> tries, each with its own <see cref="AgentAccessOAuthSettings.LoadTimeout"/>, with a
    /// jittered exponential pause between them. Only a transient failure is tried again.
    /// </summary>
    public static async Task<AgentAccessBootstrapResult> ResolveAsync(
        AgentAccessOAuthSettings? settings, int attempts, CancellationToken cancellationToken)
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

        for (var attempt = 1; ; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            var result = await TryLoadAsync(settings, settings.KeySource, prefix, issuer, issuerFailure, cancellationToken);
            if (!result.State.Transient || attempt >= attempts) return result;

            Log.Warning("Agent access keys: attempt {Attempt} of {Attempts} failed after {ElapsedMs} ms: {Reason}",
                attempt, attempts, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, result.State.Reason);
            await Task.Delay(Backoff(settings.LoadRetryDelay ?? DefaultRetryDelay, attempt, TimeSpan.MaxValue), cancellationToken);
        }
    }

    /// <summary>Full jitter: a random pause up to base x 2^(attempt-1), capped at <paramref name="max"/>.</summary>
    public static TimeSpan Backoff(TimeSpan baseDelay, int attempt, TimeSpan max)
    {
        var ceiling = Math.Min(baseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(attempt - 1, 30)), max.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * ceiling);
    }

    private static async Task<AgentAccessBootstrapResult> TryLoadAsync(
        AgentAccessOAuthSettings settings, IAgentAccessKeySource source, string prefix, Uri? issuer, string issuerFailure,
        CancellationToken cancellationToken)
    {
        (AgentAccessKeyLoadResult Load, bool Created) outcome;
        try
        {
            outcome = await LoadOrCreateAsync(
                source, prefix, settings.LoadTimeout ?? DefaultTimeout,
                settings.KeyWriteSettle ?? DefaultWriteSettle, settings.NewPrivateKeyPem ?? NewRsaPem, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return NotPersistent(null, issuer, "reading or creating the keys in SSM timed out; using ephemeral keys", transient: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Type only: the message can carry the parameter path or account details.
            return NotPersistent(null, issuer, $"reading or creating the keys in SSM failed ({ex.GetType().Name}); using ephemeral keys",
                transient: IsTransient(ex));
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

    // The timeout bounds the SSM calls only. Missing keys are generated between the first read and the writes, off
    // the clock: RSA generation is CPU work that a throttled pod at start can take seconds over.
    private static async Task<(AgentAccessKeyLoadResult Load, bool Created)> LoadOrCreateAsync(
        IAgentAccessKeySource source, string prefix, TimeSpan timeout, TimeSpan settle, Func<string> newPem,
        CancellationToken cancellationToken)
    {
        var spent = Stopwatch.StartNew();
        AgentAccessKeyLoadResult load;
        using (var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            read.CancelAfter(timeout);
            load = AgentAccessKeyLoader.Load(await source.ReadAsync(prefix, read.Token));
        }

        spent.Stop();
        if (!load.SigningMissing && !load.EncryptionMissing) return (load, false);

        (string Name, string Pem)? signing = load.SigningMissing ? NewKey(AgentAccessKeyLoader.SigningFolder, newPem) : null;
        (string Name, string Pem)? encryption = load.EncryptionMissing ? NewKey(AgentAccessKeyLoader.EncryptionFolder, newPem) : null;

        var remaining = timeout - spent.Elapsed;
        if (remaining <= TimeSpan.Zero) throw new OperationCanceledException();

        using var write = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        write.CancelAfter(remaining);
        return await CreateAsync(source, prefix, load, signing, encryption, settle, write.Token);
    }

    // Keys are only ever used as SSM gives them back, never from memory: a key no other pod can read must not sign.
    // A retry sends the same key under the same name, so a write that landed despite the error is found, not doubled.
    private static async Task<(AgentAccessKeyLoadResult Load, bool Created)> CreateAsync(
        IAgentAccessKeySource source, string prefix, AgentAccessKeyLoadResult load,
        (string Name, string Pem)? signing, (string Name, string Pem)? encryption,
        TimeSpan settle, CancellationToken cancellationToken)
    {
        for (var attempt = 1; load.SigningMissing || load.EncryptionMissing; attempt++)
        {
            try
            {
                if (load.SigningMissing && signing is { } s)
                {
                    await source.CreateAsync(prefix, s.Name, s.Pem, cancellationToken);
                }

                if (load.EncryptionMissing && encryption is { } e)
                {
                    await source.CreateAsync(prefix, e.Name, e.Pem, cancellationToken);
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

    private static (string Name, string Pem) NewKey(string folder, Func<string> newPem) =>
        ($"{folder}/{AgentAccessKeyLoader.NewKeyId(DateTime.UtcNow)}", newPem());

    private static string NewRsaPem()
    {
        using var rsa = RSA.Create(NewKeySize);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    // Worth another attempt later: the network, the credential fetch, or SSM being slow or busy. A refusal (access
    // denied, a bad path) is configuration and would fail the same way every time.
    private static bool IsTransient(Exception ex) => ex switch
    {
        AmazonServiceException service => (int)service.StatusCode >= 500 || IsContention(service) || TransientCodes.Contains(service.ErrorCode ?? ""),
        AmazonClientException or HttpRequestException or IOException or TimeoutException => true,
        _ => false
    };

    private static readonly HashSet<string> TransientCodes =
        new(["ThrottlingException", "Throttling", "TooManyUpdates", "RequestLimitExceeded"], StringComparer.Ordinal);

    // Another pod writing at the same moment, or SSM pushing back; worth a re-read and another go.
    private static bool IsContention(Exception ex) =>
        ex is ParameterAlreadyExistsException or TooManyUpdatesException
        || ex is AmazonServiceException { ErrorCode: "ThrottlingException" };

    // Transient only with a valid issuer: without one, keys read later could still never open the gate.
    private static AgentAccessBootstrapResult NotPersistent(AgentAccessKeys? keys, Uri? issuer, string reason, bool transient = false) =>
        new(keys, issuer, new AgentAccessKeyState(false, reason, null, Transient: transient && issuer is not null));

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
