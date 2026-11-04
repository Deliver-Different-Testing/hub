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
        var result = AgentAccessKeyLoaderTests.Load(Parameters(Pem(), Pem()));

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

        var first = AgentAccessKeyLoaderTests.Load(Parameters(signing, encryption));
        var second = AgentAccessKeyLoaderTests.Load(Parameters(signing, encryption));

        Assert.Equal(first.Keys!.Signing.KeyId, second.Keys!.Signing.KeyId);
    }

    [Fact]
    public void A_previous_signing_key_is_optional_and_has_its_own_kid()
    {
        var parameters = Parameters(Pem(), Pem());
        parameters[AgentAccessKeyLoader.PreviousSigningKeyName] = Pem();

        var result = AgentAccessKeyLoaderTests.Load(parameters);

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

        var result = AgentAccessKeyLoaderTests.Load(parameters);

        Assert.Null(result.Failure);
        Assert.Equal(KidOf(signing), result.Keys!.Signing.KeyId);
        Assert.Equal(KidOf(encryption), result.Keys.Encryption.KeyId);
    }

    [Fact]
    public void Hand_made_keys_come_first_as_today_then_created_keys_earliest_written_first()
    {
        var (legacy, legacyPrevious, first, second, third) = (Pem(), Pem(), Pem(), Pem(), Pem());
        var (legacyEncryption, firstEncryption, secondEncryption) = (Pem(), Pem(), Pem());
        var parameters = Parameters(legacy, legacyEncryption);
        parameters[AgentAccessKeyLoader.PreviousSigningKeyName] = legacyPrevious;
        parameters["signing/20261105010203-ffffffff"] = first;
        parameters["signing/20261106000000-00000000"] = second;
        parameters["signing/20261105010204-00000000"] = third;
        parameters["encryption/20261102000000-aaaaaaaa"] = firstEncryption;
        parameters["encryption/20261101000000-aaaaaaaa"] = secondEncryption;
        var dates = new Dictionary<string, DateTime>
        {
            ["signing/20261105010203-ffffffff"] = Written,
            ["signing/20261106000000-00000000"] = Written.AddSeconds(1),
            ["signing/20261105010204-00000000"] = Written.AddSeconds(2),
            ["encryption/20261102000000-aaaaaaaa"] = Written,
            ["encryption/20261101000000-aaaaaaaa"] = Written.AddSeconds(1)
        };

        var keys = Load(parameters, dates).Keys!;

        Assert.Equal(
            new[] { legacy, legacyPrevious, first, second, third }.Select(KidOf),
            keys.SigningKeys.Select(k => k.KeyId));
        Assert.Equal(
            new[] { legacyEncryption, firstEncryption, secondEncryption }.Select(KidOf),
            keys.EncryptionKeys.Select(k => k.KeyId));
    }

    [Theory]
    [InlineData("20261106000000-ffffffff", "20261105000000-00000000")] // earliest written has the higher kid
    [InlineData("20261105000000-00000000", "20261106000000-ffffffff")] // earliest written has the lower kid
    public void The_earliest_written_created_key_signs_and_encrypts_whatever_its_kid(string earlierKid, string laterKid)
    {
        var (earlier, later, earlierEncryption, laterEncryption) = (Pem(), Pem(), Pem(), Pem());
        var parameters = new Dictionary<string, string>
        {
            [$"signing/{earlierKid}"] = earlier,
            [$"signing/{laterKid}"] = later,
            [$"encryption/{earlierKid}"] = earlierEncryption,
            [$"encryption/{laterKid}"] = laterEncryption
        };
        var dates = new Dictionary<string, DateTime>
        {
            [$"signing/{earlierKid}"] = Written,
            [$"signing/{laterKid}"] = Written.AddMilliseconds(5),
            [$"encryption/{earlierKid}"] = Written,
            [$"encryption/{laterKid}"] = Written.AddMilliseconds(5)
        };

        var keys = Load(parameters, dates).Keys!;

        Assert.Equal(KidOf(earlier), keys.Signing.KeyId);
        Assert.Equal(KidOf(earlierEncryption), keys.Encryption.KeyId);
        Assert.Equal(2, keys.SigningKeys.Count);
    }

    [Fact]
    public void Written_at_the_same_moment_the_lower_kid_wins()
    {
        var (low, high) = (Pem(), Pem());
        var parameters = new Dictionary<string, string>
        {
            ["signing/20261105000000-bbbbbbbb"] = high,
            ["signing/20261105000000-aaaaaaaa"] = low,
            [AgentAccessKeyLoader.EncryptionKeyName] = Pem()
        };

        Assert.Equal(KidOf(low), Load(parameters).Keys!.Signing.KeyId);
    }

    [Theory]
    [InlineData("20261105000001-ffffffff", "20261105000000-00000000")] // pod B made its key in an earlier second
    [InlineData("20261105000000-00000000", "20261105000001-ffffffff")] // pod B made its key in a later second
    public void A_pod_that_sees_only_the_first_key_and_one_that_sees_both_choose_the_same_keys(string aKid, string bKid)
    {
        // Pod A wrote first and re-read before pod B's write landed; pod B re-read after both.
        var (a, b, aEncryption, bEncryption) = (Pem(), Pem(), Pem(), Pem());
        var podA = new Dictionary<string, string>
        {
            [$"signing/{aKid}"] = a,
            [$"encryption/{aKid}"] = aEncryption
        };
        var podB = new Dictionary<string, string>(podA)
        {
            [$"signing/{bKid}"] = b,
            [$"encryption/{bKid}"] = bEncryption
        };
        var dates = new Dictionary<string, DateTime>
        {
            [$"signing/{aKid}"] = Written,
            [$"encryption/{aKid}"] = Written,
            [$"signing/{bKid}"] = Written.AddSeconds(3),
            [$"encryption/{bKid}"] = Written.AddSeconds(3)
        };

        var seenByA = Load(podA, dates).Keys!;
        var seenByB = Load(podB, dates).Keys!;

        Assert.Equal(seenByA.Signing.KeyId, seenByB.Signing.KeyId);
        Assert.Equal(seenByA.Encryption.KeyId, seenByB.Encryption.KeyId);
        Assert.Contains(KidOf(b), seenByB.SigningKeys.Select(k => k.KeyId));
    }

    [Fact]
    public void Parameters_in_the_key_folders_without_a_kid_name_are_ignored()
    {
        var parameters = Parameters(Pem(), Pem());
        parameters["signing/notes"] = "ignored";
        parameters["signing/2026-11-05"] = "ignored";

        var result = AgentAccessKeyLoaderTests.Load(parameters);

        Assert.Single(result.Keys!.SigningKeys);
    }

    [Fact]
    public void A_bad_generated_key_fails_and_names_it_without_echoing_the_value()
    {
        var parameters = Parameters(Pem(), Pem());
        parameters["signing/20261105010203-0a1b2c3d"] = "-----BEGIN PRIVATE KEY-----\nnot-a-real-key-MIIEvQIBADANBg\n-----END PRIVATE KEY-----";

        var result = AgentAccessKeyLoaderTests.Load(parameters);

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

        var result = AgentAccessKeyLoaderTests.Load(parameters);

        Assert.Null(result.Keys);
        Assert.Equal(signingMissing, result.SigningMissing);
        Assert.Equal(encryptionMissing, result.EncryptionMissing);
    }

    [Fact]
    public void A_present_but_unusable_key_is_not_reported_as_missing()
    {
        using var small = RSA.Create(1024);
        var result = AgentAccessKeyLoaderTests.Load(new Dictionary<string, string>
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

        var result = AgentAccessKeyLoaderTests.Load(parameters);

        Assert.Null(result.Keys);
        Assert.Contains(missing, result.Failure);
    }

    [Fact]
    public void Unparsable_pem_fails_without_echoing_the_value()
    {
        var parameters = Parameters(Pem(), "-----BEGIN PRIVATE KEY-----\nnot-a-real-key-MIIEvQIBADANBg\n-----END PRIVATE KEY-----");

        var result = AgentAccessKeyLoaderTests.Load(parameters);

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

        var result = AgentAccessKeyLoaderTests.Load(parameters);

        Assert.Null(result.Keys);
        Assert.Contains(AgentAccessKeyLoader.SigningKeyName, result.Failure);
    }

    [Fact]
    public void Keys_smaller_than_2048_bits_are_rejected()
    {
        using var small = RSA.Create(1024);
        var parameters = Parameters(small.ExportPkcs8PrivateKeyPem(), Pem());

        var result = AgentAccessKeyLoaderTests.Load(parameters);

        Assert.Null(result.Keys);
        Assert.Contains("2048", result.Failure);
    }

    [Fact]
    public void Unrelated_parameters_under_the_prefix_are_ignored()
    {
        var parameters = Parameters(Pem(), Pem());
        parameters["SomethingElse"] = "ignored";

        Assert.NotNull(AgentAccessKeyLoaderTests.Load(parameters).Keys);
    }

    // ---- helpers ----------------------------------------------------------

    internal static string Pem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    internal static readonly DateTime Written = new(2026, 11, 5, 1, 0, 0, DateTimeKind.Utc);

    // As SSM returns them: every value with its last-modified date (Written unless given).
    internal static Dictionary<string, AgentAccessKeyParameter> Stored(
        IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, DateTime>? dates = null) =>
        values.ToDictionary(p => p.Key, p => new AgentAccessKeyParameter(p.Value, dates?.GetValueOrDefault(p.Key, Written) ?? Written));

    internal static AgentAccessKeyLoadResult Load(
        IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, DateTime>? dates = null) =>
        AgentAccessKeyLoader.Load(Stored(values, dates));

    internal static string KidOf(string pem) =>
        AgentAccessKeyLoaderTests.Load(Parameters(pem, Pem())).Keys!.Signing.KeyId;

    internal static Dictionary<string, string> Parameters(string signing, string encryption) => new()
    {
        [AgentAccessKeyLoader.SigningKeyName] = signing,
        [AgentAccessKeyLoader.EncryptionKeyName] = encryption
    };
}
