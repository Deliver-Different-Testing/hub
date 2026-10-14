using System.Security.Cryptography;
using Hub.Interfaces;
using Microsoft.IdentityModel.Tokens;
using Hub.Services;

namespace Hub.Features.AgentAccess;

public sealed record AgentAccessKeyState(bool Persistent, string Reason, string? SigningKeyId) : IAgentAccessKeyState;

public sealed record AgentAccessKeys(RsaSecurityKey Signing, RsaSecurityKey Encryption, RsaSecurityKey? PreviousSigning);

public sealed record AgentAccessKeyLoadResult(AgentAccessKeys? Keys, string? Failure);

/// <summary>
/// Turns the SSM parameters into RSA keys. Failure messages name the parameter and the rule
/// that failed; they never contain the PEM text or any part of a key.
/// </summary>
public static class AgentAccessKeyLoader
{
    public const string SigningKeyName = "SigningKey";
    public const string EncryptionKeyName = "EncryptionKey";
    public const string PreviousSigningKeyName = "SigningKeyPrevious";

    private const int MinimumKeySize = 2048;

    public static AgentAccessKeyLoadResult Load(IReadOnlyDictionary<string, string> parameters)
    {
        var (signing, signingFailure) = Parse(parameters, SigningKeyName, required: true);
        if (signingFailure is not null) return new AgentAccessKeyLoadResult(null, signingFailure);

        var (encryption, encryptionFailure) = Parse(parameters, EncryptionKeyName, required: true);
        if (encryptionFailure is not null) return new AgentAccessKeyLoadResult(null, encryptionFailure);

        var (previous, previousFailure) = Parse(parameters, PreviousSigningKeyName, required: false);
        if (previousFailure is not null) return new AgentAccessKeyLoadResult(null, previousFailure);

        return new AgentAccessKeyLoadResult(new AgentAccessKeys(signing!, encryption!, previous), null);
    }

    private static (RsaSecurityKey? Key, string? Failure) Parse(
        IReadOnlyDictionary<string, string> parameters, string name, bool required)
    {
        if (!parameters.TryGetValue(name, out var pem) || string.IsNullOrWhiteSpace(pem))
        {
            return required ? (null, $"{name} is missing") : (null, null);
        }

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
}
