using System.Security.Cryptography;
using Hub.Services;
using Hub.Tests.Services;
using Hub.Features.AgentAccess;

namespace Hub.Tests.Features.AgentAccess;

// Phase 1.3: the OAuth signing and encryption keys come from SSM as PEM text. The loader
// turns the parameters into keys, and must never put key material into a failure message.
public class AgentAccessKeyLoaderTests
{
    [Fact]
    public void Valid_parameters_load_signing_and_encryption_keys_with_distinct_kids()
    {
        var result = AgentAccessKeyLoader.Load(Parameters(Pem(), Pem()));

        Assert.NotNull(result.Keys);
        Assert.Null(result.Failure);
        Assert.False(string.IsNullOrEmpty(result.Keys!.Signing.KeyId));
        Assert.False(string.IsNullOrEmpty(result.Keys.Encryption.KeyId));
        Assert.NotEqual(result.Keys.Signing.KeyId, result.Keys.Encryption.KeyId);
        Assert.Null(result.Keys.PreviousSigning);
    }

    [Fact]
    public void The_kid_is_stable_for_the_same_key()
    {
        var signing = Pem();
        var encryption = Pem();

        var first = AgentAccessKeyLoader.Load(Parameters(signing, encryption));
        var second = AgentAccessKeyLoader.Load(Parameters(signing, encryption));

        Assert.Equal(first.Keys!.Signing.KeyId, second.Keys!.Signing.KeyId);
    }

    [Fact]
    public void A_previous_signing_key_is_optional_and_has_its_own_kid()
    {
        var parameters = Parameters(Pem(), Pem());
        parameters[AgentAccessKeyLoader.PreviousSigningKeyName] = Pem();

        var result = AgentAccessKeyLoader.Load(parameters);

        Assert.NotNull(result.Keys!.PreviousSigning);
        Assert.NotEqual(result.Keys.Signing.KeyId, result.Keys.PreviousSigning!.KeyId);
    }

    [Theory]
    [InlineData(AgentAccessKeyLoader.SigningKeyName)]
    [InlineData(AgentAccessKeyLoader.EncryptionKeyName)]
    public void A_missing_required_key_fails_and_names_it(string missing)
    {
        var parameters = Parameters(Pem(), Pem());
        parameters.Remove(missing);

        var result = AgentAccessKeyLoader.Load(parameters);

        Assert.Null(result.Keys);
        Assert.Contains(missing, result.Failure);
    }

    [Fact]
    public void Unparsable_pem_fails_without_echoing_the_value()
    {
        var parameters = Parameters(Pem(), "-----BEGIN PRIVATE KEY-----\nnot-a-real-key-MIIEvQIBADANBg\n-----END PRIVATE KEY-----");

        var result = AgentAccessKeyLoader.Load(parameters);

        Assert.Null(result.Keys);
        Assert.Contains(AgentAccessKeyLoader.EncryptionKeyName, result.Failure);
        Assert.DoesNotContain("MIIEvQIBADANBg", result.Failure);
        Assert.DoesNotContain("BEGIN", result.Failure);
    }

    [Fact]
    public void A_public_only_pem_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var parameters = Parameters(rsa.ExportSubjectPublicKeyInfoPem(), Pem());

        var result = AgentAccessKeyLoader.Load(parameters);

        Assert.Null(result.Keys);
        Assert.Contains(AgentAccessKeyLoader.SigningKeyName, result.Failure);
    }

    [Fact]
    public void Keys_smaller_than_2048_bits_are_rejected()
    {
        using var small = RSA.Create(1024);
        var parameters = Parameters(small.ExportPkcs8PrivateKeyPem(), Pem());

        var result = AgentAccessKeyLoader.Load(parameters);

        Assert.Null(result.Keys);
        Assert.Contains("2048", result.Failure);
    }

    [Fact]
    public void Unrelated_parameters_under_the_prefix_are_ignored()
    {
        var parameters = Parameters(Pem(), Pem());
        parameters["SomethingElse"] = "ignored";

        Assert.NotNull(AgentAccessKeyLoader.Load(parameters).Keys);
    }

    // ---- helpers ----------------------------------------------------------

    internal static string Pem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    internal static Dictionary<string, string> Parameters(string signing, string encryption) => new()
    {
        [AgentAccessKeyLoader.SigningKeyName] = signing,
        [AgentAccessKeyLoader.EncryptionKeyName] = encryption
    };
}
