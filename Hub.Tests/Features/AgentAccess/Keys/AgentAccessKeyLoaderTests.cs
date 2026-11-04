using System.Security.Cryptography;
using Hub.Features.AgentAccess.Keys;

namespace Hub.Tests.Features.AgentAccess.Keys;

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
        Assert.Single(result.Keys.SigningKeys);
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

        Assert.Equal(2, result.Keys!.SigningKeys.Count);
        Assert.NotEqual(result.Keys.Signing.KeyId, result.Keys.SigningKeys[1].KeyId);
    }

    [Fact]
    public void Generated_keys_load_from_their_kid_named_parameters()
    {
        var signing = Pem();
        var encryption = Pem();
        var parameters = new Dictionary<string, string>
        {
            [$"{AgentAccessKeyLoader.SigningFolder}/20261105010203-0a1b2c3d"] = signing,
            [$"{AgentAccessKeyLoader.EncryptionFolder}/20261105010203-4e5f6a7b"] = encryption
        };

        var result = AgentAccessKeyLoader.Load(parameters);

        Assert.Null(result.Failure);
        Assert.Equal(KidOf(signing), result.Keys!.Signing.KeyId);
        Assert.Equal(KidOf(encryption), result.Keys.Encryption.KeyId);
    }

    [Fact]
    public void Every_key_loads_newest_first_by_kid_timestamp_and_hand_made_keys_count_as_oldest()
    {
        var (legacy, legacyPrevious, older, newer, newest) = (Pem(), Pem(), Pem(), Pem(), Pem());
        var (legacyEncryption, olderEncryption, newerEncryption) = (Pem(), Pem(), Pem());
        var parameters = Parameters(legacy, legacyEncryption);
        parameters[AgentAccessKeyLoader.PreviousSigningKeyName] = legacyPrevious;
        parameters["signing/20261105010203-ffffffff"] = older;
        parameters["signing/20261106000000-00000000"] = newest;
        parameters["signing/20261105010204-00000000"] = newer;
        parameters["encryption/20261101000000-aaaaaaaa"] = olderEncryption;
        parameters["encryption/20261102000000-aaaaaaaa"] = newerEncryption;

        var keys = AgentAccessKeyLoader.Load(parameters).Keys!;

        Assert.Equal(
            new[] { newest, newer, older, legacy, legacyPrevious }.Select(KidOf),
            keys.SigningKeys.Select(k => k.KeyId));
        Assert.Equal(
            new[] { newerEncryption, olderEncryption, legacyEncryption }.Select(KidOf),
            keys.EncryptionKeys.Select(k => k.KeyId));
    }

    [Fact]
    public void Parameters_in_the_key_folders_without_a_kid_name_are_ignored()
    {
        var parameters = Parameters(Pem(), Pem());
        parameters["signing/notes"] = "ignored";
        parameters["signing/2026-11-05"] = "ignored";

        var result = AgentAccessKeyLoader.Load(parameters);

        Assert.Single(result.Keys!.SigningKeys);
    }

    [Fact]
    public void A_bad_generated_key_fails_and_names_it_without_echoing_the_value()
    {
        var parameters = Parameters(Pem(), Pem());
        parameters["signing/20261105010203-0a1b2c3d"] = "-----BEGIN PRIVATE KEY-----\nnot-a-real-key-MIIEvQIBADANBg\n-----END PRIVATE KEY-----";

        var result = AgentAccessKeyLoader.Load(parameters);

        Assert.Null(result.Keys);
        Assert.Contains("signing/20261105010203-0a1b2c3d", result.Failure);
        Assert.DoesNotContain("MIIEvQIBADANBg", result.Failure);
        Assert.False(result.SigningMissing);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Missing_kinds_are_reported_so_they_can_be_created(bool signingMissing, bool encryptionMissing)
    {
        var parameters = new Dictionary<string, string>();
        if (!signingMissing) parameters[AgentAccessKeyLoader.SigningKeyName] = Pem();
        if (!encryptionMissing) parameters["encryption/20261105010203-0a1b2c3d"] = Pem();

        var result = AgentAccessKeyLoader.Load(parameters);

        Assert.Null(result.Keys);
        Assert.Equal(signingMissing, result.SigningMissing);
        Assert.Equal(encryptionMissing, result.EncryptionMissing);
    }

    [Fact]
    public void A_present_but_unusable_key_is_not_reported_as_missing()
    {
        using var small = RSA.Create(1024);
        var result = AgentAccessKeyLoader.Load(new Dictionary<string, string>
        {
            [AgentAccessKeyLoader.SigningKeyName] = small.ExportPkcs8PrivateKeyPem()
        });

        Assert.Null(result.Keys);
        Assert.False(result.SigningMissing);
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

    internal static string KidOf(string pem) =>
        AgentAccessKeyLoader.Load(Parameters(pem, Pem())).Keys!.Signing.KeyId;

    internal static Dictionary<string, string> Parameters(string signing, string encryption) => new()
    {
        [AgentAccessKeyLoader.SigningKeyName] = signing,
        [AgentAccessKeyLoader.EncryptionKeyName] = encryption
    };
}
