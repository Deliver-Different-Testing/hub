using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;

namespace Hub.Features.AgentAccess.Keys;

public sealed record AgentAccessKeyState(bool Persistent, string Reason, string? SigningKeyId, string? EncryptionKeyId = null)
    : IAgentAccessKeyState;

/// <summary>
/// Every key under the prefix, in the order they are used: the first signing key signs and the first
/// encryption key encrypts; the rest are only published or accepted.
/// </summary>
public sealed record AgentAccessKeys(IReadOnlyList<RsaSecurityKey> SigningKeys, IReadOnlyList<RsaSecurityKey> EncryptionKeys)
{
    public RsaSecurityKey Signing => SigningKeys[0];

    public RsaSecurityKey Encryption => EncryptionKeys[0];
}

/// <param name="SigningMissing">No signing key exists at all, so one may be created. False when one exists but is unusable.</param>
/// <param name="EncryptionMissing">The same for the encryption key.</param>
public sealed record AgentAccessKeyLoadResult(
    AgentAccessKeys? Keys, string? Failure, bool SigningMissing = false, bool EncryptionMissing = false);

/// <summary>
/// Turns the SSM parameters into RSA keys. Two layouts sit side by side under the prefix: the hand-made
/// SigningKey, EncryptionKey and SigningKeyPrevious, used first exactly as before, and the keys Hub creates
/// itself under signing/&lt;kid&gt; and encryption/&lt;kid&gt;, earliest written first. Failure messages name
/// the parameter and the rule that failed; they never contain the PEM text or any part of a key.
/// </summary>
public static partial class AgentAccessKeyLoader
{
    public const string SigningKeyName = "SigningKey";
    public const string EncryptionKeyName = "EncryptionKey";
    public const string PreviousSigningKeyName = "SigningKeyPrevious";
    public const string SigningFolder = "signing";
    public const string EncryptionFolder = "encryption";

    private const int MinimumKeySize = 2048;

    public static AgentAccessKeyLoadResult Load(IReadOnlyDictionary<string, AgentAccessKeyParameter> parameters)
    {
        var (signing, signingFailure) = Collect(parameters, SigningFolder, SigningKeyName, PreviousSigningKeyName);
        if (signingFailure is not null) return new AgentAccessKeyLoadResult(null, signingFailure);

        var (encryption, encryptionFailure) = Collect(parameters, EncryptionFolder, EncryptionKeyName);
        if (encryptionFailure is not null) return new AgentAccessKeyLoadResult(null, encryptionFailure);

        if (signing.Count == 0 || encryption.Count == 0)
        {
            return new AgentAccessKeyLoadResult(
                null,
                signing.Count == 0 ? $"{SigningKeyName} is missing" : $"{EncryptionKeyName} is missing",
                SigningMissing: signing.Count == 0,
                EncryptionMissing: encryption.Count == 0);
        }

        return new AgentAccessKeyLoadResult(new AgentAccessKeys(signing, encryption), null);
    }

    /// <summary>A created key's name below its folder: UTC creation time, then random hex so racing pods never collide.</summary>
    public static string NewKeyId(DateTime utcNow) =>
        $"{utcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}";

    // Hand-made keys first, as before. Then created keys by the date SSM wrote them, earliest first, kid as the
    // tie-break: every pod that sees a later key also sees the earlier one, so pods that raced to create keys
    // at first start still agree on which one signs and encrypts.
    private static (List<RsaSecurityKey> Keys, string? Failure) Collect(
        IReadOnlyDictionary<string, AgentAccessKeyParameter> parameters, string folder, params string[] handMade)
    {
        var created = parameters
            .Where(p => p.Key.StartsWith(folder + "/", StringComparison.Ordinal)
                        && KeyIdPattern().IsMatch(p.Key[(folder.Length + 1)..]))
            .OrderBy(p => p.Value.LastModified)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key);
        var names = handMade.Where(parameters.ContainsKey).Concat(created);

        var keys = new List<RsaSecurityKey>();
        foreach (var name in names)
        {
            var (key, failure) = Parse(name, parameters[name].Value);
            if (failure is not null) return (keys, failure);
            if (key is not null) keys.Add(key);
        }

        return (keys, null);
    }

    private static (RsaSecurityKey? Key, string? Failure) Parse(string name, string pem)
    {
        if (string.IsNullOrWhiteSpace(pem)) return (null, null);

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            if (rsa.KeySize < MinimumKeySize)
            {
                rsa.Dispose();
                return (null, $"{name} must be an RSA key of at least {MinimumKeySize} bits");
            }

            // Throws for a public-only key.
            rsa.ExportParameters(includePrivateParameters: true);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            rsa.Dispose();
            return (null, $"{name} is not a valid RSA private key in PEM form");
        }

        var key = new RsaSecurityKey(rsa);
        key.KeyId = Thumbprint(key);
        return (key, null);
    }

    // RFC 7638 JWK thumbprint of the public key: stable for the same key, different per key.
    private static string Thumbprint(RsaSecurityKey key) =>
        Base64UrlEncoder.Encode(JsonWebKeyConverter.ConvertFromRSASecurityKey(key).ComputeJwkThumbprint());

    [GeneratedRegex("^[0-9]{14}-[0-9a-f]{8}$")]
    private static partial Regex KeyIdPattern();
}

// Whether the OAuth server runs on persistent keys from SSM and a configured issuer
// (phase 1.3). Decided once at startup. When it is not persistent Hub still starts, on
// per-pod ephemeral keys, but the agent access gate stays closed: tokens signed with a
// key another pod cannot verify must never be issued.
public interface IAgentAccessKeyState
{
    bool Persistent { get; }

    /// <summary>Why the keys are, or are not, persistent. Never contains key material.</summary>
    string Reason { get; }

    /// <summary>Key id of the current signing key when persistent; safe to log.</summary>
    string? SigningKeyId { get; }

    /// <summary>Key id of the current encryption key when persistent; safe to log.</summary>
    string? EncryptionKeyId { get; }
}
