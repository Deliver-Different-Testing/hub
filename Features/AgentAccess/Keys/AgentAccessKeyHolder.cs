using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Hub.Features.AgentAccess.Keys;

/// <summary>
/// The keys OpenIddict signs and encrypts with, and whether they are persistent. Starts from the startup result; when
/// that was a transient SSM failure, <see cref="TryRecover"/> later swaps in the persistent keys and signals OpenIddict
/// to rebuild its options, so the pod recovers without a restart.
/// </summary>
public sealed class AgentAccessKeyHolder : IAgentAccessKeyState, IOptionsChangeTokenSource<OpenIddictServerOptions>
{
    private const int EphemeralKeySize = 2048;

    private readonly Lock _lock = new();
    private Snapshot _current;
    private CancellationTokenSource _changed = new();

    private sealed record Snapshot(AgentAccessKeys Keys, AgentAccessKeyState State);

    // Keys SSM gave back are used even when the issuer is missing, as before; otherwise per-pod ephemeral keys,
    // made once so an options rebuild keeps them.
    public AgentAccessKeyHolder(AgentAccessBootstrapResult startup) =>
        _current = new Snapshot(startup.Keys ?? Ephemeral(), startup.State);

    public AgentAccessKeys Keys => Volatile.Read(ref _current).Keys;

    public bool Persistent => Volatile.Read(ref _current).State.Persistent;

    public string Reason => Volatile.Read(ref _current).State.Reason;

    public string? SigningKeyId => Volatile.Read(ref _current).State.SigningKeyId;

    public string? EncryptionKeyId => Volatile.Read(ref _current).State.EncryptionKeyId;

    public bool Transient => Volatile.Read(ref _current).State.Transient;

    /// <summary>Takes persistent keys once; false when <paramref name="result"/> is not persistent or keys already are.</summary>
    public bool TryRecover(AgentAccessBootstrapResult result)
    {
        if (!result.State.Persistent || result.Keys is null) return false;

        CancellationTokenSource changed;
        lock (_lock)
        {
            if (_current.State.Persistent) return false;

            Volatile.Write(ref _current, new Snapshot(result.Keys, result.State));
            changed = _changed;
            _changed = new CancellationTokenSource();
        }

        changed.Cancel();
        return true;
    }

    string IOptionsChangeTokenSource<OpenIddictServerOptions>.Name => Options.DefaultName;

    public IChangeToken GetChangeToken()
    {
        lock (_lock)
        {
            return new CancellationChangeToken(_changed.Token);
        }
    }

    private static AgentAccessKeys Ephemeral() =>
        new([new RsaSecurityKey(RSA.Create(EphemeralKeySize))], [new RsaSecurityKey(RSA.Create(EphemeralKeySize))]);
}

/// <summary>Gives OpenIddict the holder's keys each time its options are built.</summary>
public sealed class AgentAccessServerCredentials(AgentAccessKeyHolder holder) : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        var keys = holder.Keys;

        // In loader order: OpenIddict signs and encrypts with the first credential; the rest are only
        // published (signing) or accepted (encryption).
        foreach (var signing in keys.SigningKeys)
        {
            options.SigningCredentials.Add(new SigningCredentials(signing, SecurityAlgorithms.RsaSha256));
        }

        foreach (var encryption in keys.EncryptionKeys)
        {
            options.EncryptionCredentials.Add(
                new EncryptingCredentials(encryption, SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
        }
    }
}
