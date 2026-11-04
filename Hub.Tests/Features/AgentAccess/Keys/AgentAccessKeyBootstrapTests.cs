using System.Globalization;
using System.Security.Cryptography;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Hub.Features.AgentAccess.Keys;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Keys;

// Phase 1.3: decides, once at startup, whether OAuth runs on persistent keys from SSM and a
// configured issuer. Anything short of that is "not persistent": Hub still starts (on
// ephemeral keys) but the agent access gate stays closed.
public class AgentAccessKeyBootstrapTests
{
    private const string Prefix = "/Hub/urgent/AgentAccess";
    private const string Issuer = "https://hub.urgent.example.test";

    [Fact]
    public void Without_settings_keys_are_not_persistent()
    {
        var result = AgentAccessKeyBootstrap.Resolve(null);

        Assert.False(result.State.Persistent);
        Assert.Null(result.Keys);
        Assert.Contains("AgentAccessSsmPrefix", result.State.Reason);
    }

    [Fact]
    public void An_unset_prefix_is_not_persistent()
    {
        var result = AgentAccessKeyBootstrap.Resolve(
            new AgentAccessOAuthSettings(SsmPrefix: null, Issuer: Issuer, KeySource: Source()));

        Assert.False(result.State.Persistent);
        Assert.Contains("AgentAccessSsmPrefix", result.State.Reason);
    }

    [Fact]
    public void A_prefix_with_no_key_source_is_not_persistent()
    {
        var result = AgentAccessKeyBootstrap.Resolve(
            new AgentAccessOAuthSettings(Prefix, Issuer, KeySource: null));

        Assert.False(result.State.Persistent);
    }

    [Fact]
    public void Valid_keys_and_https_issuer_are_persistent()
    {
        var result = AgentAccessKeyBootstrap.Resolve(
            new AgentAccessOAuthSettings(Prefix, Issuer, Source()));

        Assert.True(result.State.Persistent);
        Assert.NotNull(result.Keys);
        Assert.Equal(result.Keys!.Signing.KeyId, result.State.SigningKeyId);
        Assert.Equal(new Uri(Issuer), result.Issuer);
    }

    [Fact]
    public void The_prefix_is_passed_to_the_source()
    {
        var source = Source();

        AgentAccessKeyBootstrap.Resolve(new AgentAccessOAuthSettings(Prefix, Issuer, source));

        source.Received(1).ReadAsync(Prefix, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://hub.urgent.example.test")]       // https only
    [InlineData("https://hub.urgent.example.test/path")] // an origin, not a path
    public void A_missing_or_invalid_issuer_is_not_persistent_but_the_keys_are_still_used(string? issuer)
    {
        var result = AgentAccessKeyBootstrap.Resolve(
            new AgentAccessOAuthSettings(Prefix, issuer, Source()));

        Assert.False(result.State.Persistent);
        Assert.Contains("AgentAccessIssuer", result.State.Reason);
        Assert.NotNull(result.Keys);
        Assert.Null(result.Issuer);
    }

    [Fact]
    public void A_trailing_slash_on_the_issuer_is_accepted()
    {
        var result = AgentAccessKeyBootstrap.Resolve(
            new AgentAccessOAuthSettings(Prefix, Issuer + "/", Source()));

        Assert.True(result.State.Persistent);
        Assert.Equal(new Uri(Issuer), result.Issuer);
    }

    [Fact]
    public void A_missing_key_is_not_persistent_and_the_reason_names_it_without_key_material()
    {
        var parameters = AgentAccessKeyLoaderTests.Parameters(AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());
        parameters.Remove(AgentAccessKeyLoader.EncryptionKeyName);

        var result = AgentAccessKeyBootstrap.Resolve(
            new AgentAccessOAuthSettings(Prefix, Issuer, Source(parameters)));

        Assert.False(result.State.Persistent);
        Assert.Null(result.Keys);
        Assert.Contains(AgentAccessKeyLoader.EncryptionKeyName, result.State.Reason);
    }

    [Fact]
    public void An_ssm_failure_is_not_persistent_and_does_not_throw()
    {
        var source = Substitute.For<IAgentAccessKeySource>();
        source.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyDictionary<string, AgentAccessKeyParameter>>(_ => throw new InvalidOperationException("AccessDenied secret-detail"));

        var result = AgentAccessKeyBootstrap.Resolve(new AgentAccessOAuthSettings(Prefix, Issuer, source));

        Assert.False(result.State.Persistent);
        Assert.Null(result.Keys);
        Assert.Contains("InvalidOperationException", result.State.Reason);
        Assert.DoesNotContain("secret-detail", result.State.Reason);
    }

    [Fact]
    public void A_slow_ssm_does_not_block_startup()
    {
        var source = Substitute.For<IAgentAccessKeySource>();
        source.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return (IReadOnlyDictionary<string, AgentAccessKeyParameter>)new Dictionary<string, AgentAccessKeyParameter>();
            });

        var result = AgentAccessKeyBootstrap.Resolve(
            new AgentAccessOAuthSettings(Prefix, Issuer, source, LoadTimeout: TimeSpan.FromMilliseconds(150)));

        Assert.False(result.State.Persistent);
        Assert.Contains("timed out", result.State.Reason);
    }

    // ---- keys create themselves --------------------------------------------

    [Fact]
    public void An_empty_prefix_gets_both_keys_created_and_loaded_back_from_ssm()
    {
        var parameters = new Dictionary<string, string>();
        var source = Store(parameters);

        var result = AgentAccessKeyBootstrap.Resolve(Settings(source));

        Assert.True(result.State.Persistent, result.State.Reason);
        Assert.Equal(2, parameters.Count);
        var signing = Assert.Single(parameters.Keys, k => k.StartsWith("signing/", StringComparison.Ordinal));
        var encryption = Assert.Single(parameters.Keys, k => k.StartsWith("encryption/", StringComparison.Ordinal));
        Assert.Matches(@"^signing/\d{14}-[0-9a-f]{8}$", signing);
        Assert.Matches(@"^encryption/\d{14}-[0-9a-f]{8}$", encryption);
        Assert.Equal(AgentAccessKeyLoaderTests.KidOf(parameters[signing]), result.State.SigningKeyId);
        source.Received(2).CreateAsync(Prefix, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        source.Received(2).ReadAsync(Prefix, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Created_keys_are_3072_bit_rsa_pem_named_by_the_utc_time_they_were_made()
    {
        var parameters = new Dictionary<string, string>();
        var before = DateTime.UtcNow;

        AgentAccessKeyBootstrap.Resolve(Settings(Store(parameters)));

        Assert.Equal(2, parameters.Count);
        foreach (var (name, pem) in parameters)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            Assert.Equal(3072, rsa.KeySize);
            var made = DateTime.ParseExact(
                name.Split('/')[1][..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            Assert.InRange(made, before.AddSeconds(-2), DateTime.UtcNow.AddSeconds(1));
        }
    }

    [Fact]
    public void Keys_are_used_only_once_ssm_gives_them_back()
    {
        // A write that SSM does not keep: the pod must not sign with a key no other pod can read.
        var source = Substitute.For<IAgentAccessKeySource>();
        IReadOnlyDictionary<string, AgentAccessKeyParameter> empty = new Dictionary<string, AgentAccessKeyParameter>();
        source.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(empty);

        var result = AgentAccessKeyBootstrap.Resolve(Settings(source));

        Assert.False(result.State.Persistent);
        Assert.Null(result.Keys);
        Assert.Contains("missing", result.State.Reason);
    }

    [Fact]
    public void Hand_made_keys_load_and_nothing_is_written()
    {
        var source = Store(AgentAccessKeyLoaderTests.Parameters(AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem()));

        var result = AgentAccessKeyBootstrap.Resolve(Settings(source));

        Assert.True(result.State.Persistent);
        source.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Created_keys_already_in_ssm_load_and_nothing_is_written()
    {
        var source = Store(new Dictionary<string, string>
        {
            ["signing/20261105010203-0a1b2c3d"] = AgentAccessKeyLoaderTests.Pem(),
            ["encryption/20261105010203-4e5f6a7b"] = AgentAccessKeyLoaderTests.Pem()
        });

        var result = AgentAccessKeyBootstrap.Resolve(Settings(source));

        Assert.True(result.State.Persistent);
        source.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Only_the_missing_key_is_created()
    {
        var signing = AgentAccessKeyLoaderTests.Pem();
        var parameters = new Dictionary<string, string> { [AgentAccessKeyLoader.SigningKeyName] = signing };

        var result = AgentAccessKeyBootstrap.Resolve(Settings(Store(parameters)));

        Assert.True(result.State.Persistent, result.State.Reason);
        Assert.Equal(AgentAccessKeyLoaderTests.KidOf(signing), result.State.SigningKeyId);
        Assert.Matches(@"^encryption/\d{14}-[0-9a-f]{8}$", Assert.Single(parameters.Keys, k => k != AgentAccessKeyLoader.SigningKeyName));
    }

    [Fact]
    public void A_present_but_unusable_key_is_not_papered_over_with_a_new_one()
    {
        using var small = RSA.Create(1024);
        var source = Store(new Dictionary<string, string> { [AgentAccessKeyLoader.SigningKeyName] = small.ExportPkcs8PrivateKeyPem() });

        var result = AgentAccessKeyBootstrap.Resolve(Settings(source));

        Assert.False(result.State.Persistent);
        Assert.Contains("2048", result.State.Reason);
        source.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void When_another_pod_got_there_first_its_keys_are_loaded()
    {
        var parameters = new Dictionary<string, string>();
        var otherSigning = AgentAccessKeyLoaderTests.Pem();
        var source = Store(parameters);
        source.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ =>
            {
                parameters["signing/20261105010203-0a1b2c3d"] = otherSigning;
                parameters["encryption/20261105010203-4e5f6a7b"] = AgentAccessKeyLoaderTests.Pem();
                throw new ParameterAlreadyExistsException("exists");
            });

        var result = AgentAccessKeyBootstrap.Resolve(Settings(source));

        Assert.True(result.State.Persistent, result.State.Reason);
        Assert.Equal(AgentAccessKeyLoaderTests.KidOf(otherSigning), result.State.SigningKeyId);
        source.Received(1).CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Pods_that_both_create_keys_on_first_start_settle_on_the_same_ones()
    {
        var shared = new Dictionary<string, string>();
        var dates = new Dictionary<string, DateTime>();

        // Pod A writes first and re-reads before pod B's write lands: it sees only its own keys.
        var podA = AgentAccessKeyBootstrap.Resolve(Settings(Store(shared, dates, writtenAt: AgentAccessKeyLoaderTests.Written)));
        // Pod B read the prefix before A wrote, so it writes too, later, then re-reads and sees both.
        var podB = AgentAccessKeyBootstrap.Resolve(Settings(
            Store(shared, dates, writtenAt: AgentAccessKeyLoaderTests.Written.AddSeconds(3), staleFirstRead: true)));

        Assert.True(podA.State.Persistent, podA.State.Reason);
        Assert.True(podB.State.Persistent, podB.State.Reason);
        Assert.Equal(4, shared.Count);
        Assert.Equal(podA.State.SigningKeyId, podB.State.SigningKeyId);
        Assert.Equal(podA.State.EncryptionKeyId, podB.State.EncryptionKeyId);
        Assert.Equal(2, podB.Keys!.SigningKeys.Count);
    }

    [Fact]
    public void The_state_names_both_the_signing_and_the_encryption_kid()
    {
        var (signing, encryption) = (AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());

        var result = AgentAccessKeyBootstrap.Resolve(Settings(Store(AgentAccessKeyLoaderTests.Parameters(signing, encryption))));

        Assert.Equal(AgentAccessKeyLoaderTests.KidOf(signing), result.State.SigningKeyId);
        Assert.Equal(AgentAccessKeyLoaderTests.KidOf(encryption), result.State.EncryptionKeyId);
    }

    [Fact]
    public void Hand_made_keys_still_sign_and_encrypt_when_created_keys_sit_beside_them()
    {
        var (signing, encryption) = (AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());
        var parameters = AgentAccessKeyLoaderTests.Parameters(signing, encryption);
        parameters["signing/20261105010203-0a1b2c3d"] = AgentAccessKeyLoaderTests.Pem();
        parameters["encryption/20261105010203-0a1b2c3d"] = AgentAccessKeyLoaderTests.Pem();

        var result = AgentAccessKeyBootstrap.Resolve(Settings(Store(parameters)));

        Assert.Equal(AgentAccessKeyLoaderTests.KidOf(signing), result.State.SigningKeyId);
        Assert.Equal(AgentAccessKeyLoaderTests.KidOf(encryption), result.State.EncryptionKeyId);
    }

    [Theory]
    [InlineData("TooManyUpdatesException")]
    [InlineData("ThrottlingException")]
    public void A_write_that_keeps_being_throttled_is_not_persistent_and_does_not_throw(string error)
    {
        var source = Store(new Dictionary<string, string>());
        source.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw (error == "TooManyUpdatesException"
                ? new TooManyUpdatesException("busy")
                : new AmazonSimpleSystemsManagementException("slow down") { ErrorCode = "ThrottlingException" }));

        var result = AgentAccessKeyBootstrap.Resolve(Settings(source));

        Assert.False(result.State.Persistent);
        Assert.Null(result.Keys);
        source.Received(3).CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_refused_write_is_not_persistent_at_once_and_the_reason_has_no_path_or_detail()
    {
        var source = Store(new Dictionary<string, string>());
        source.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new AmazonSimpleSystemsManagementException($"not allowed on {Prefix}/signing secret-detail") { ErrorCode = "AccessDeniedException" });

        var result = AgentAccessKeyBootstrap.Resolve(Settings(source));

        Assert.False(result.State.Persistent);
        Assert.DoesNotContain("secret-detail", result.State.Reason);
        Assert.DoesNotContain(Prefix, result.State.Reason);
        source.Received(1).CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_startup_timeout_covers_the_write()
    {
        var source = Store(new Dictionary<string, string>());
        source.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()));

        var result = AgentAccessKeyBootstrap.Resolve(
            new AgentAccessOAuthSettings(Prefix, Issuer, source, LoadTimeout: TimeSpan.FromMilliseconds(300), KeyWriteSettle: TimeSpan.Zero));

        Assert.False(result.State.Persistent);
        Assert.Contains("timed out", result.State.Reason);
    }

    // ---- prefix -----------------------------------------------------------

    [Theory]
    [InlineData("https://hub.urgent.deliverdifferent.com", "/hub/prod/urgent/agent-access")]
    [InlineData("https://hub.urgent.staging.deliverdifferent.com", "/hub/staging/urgent/agent-access")]
    [InlineData("https://hub.dfrnt.deliverdifferent.com", "/hub/prod/dfrnt/agent-access")]
    [InlineData("https://hub.staging.deliverdifferent.com", "/hub/staging/dfrnt/agent-access")]
    public void Without_a_configured_prefix_it_comes_from_the_issuers_environment_and_tenant(string issuer, string expected)
    {
        var source = Store(new Dictionary<string, string>());

        var result = AgentAccessKeyBootstrap.Resolve(Settings(source, prefix: null, issuer: issuer));

        Assert.True(result.State.Persistent, result.State.Reason);
        source.Received(2).ReadAsync(expected, Arg.Any<CancellationToken>());
        Assert.Equal(expected, AgentAccessKeyBootstrap.ResolvePrefix(null, issuer));
    }

    [Fact]
    public void A_configured_prefix_wins_over_the_issuer()
    {
        var source = Source();

        AgentAccessKeyBootstrap.Resolve(Settings(source, prefix: Prefix, issuer: "https://hub.urgent.deliverdifferent.com"));

        source.Received(1).ReadAsync(Prefix, Arg.Any<CancellationToken>());
        Assert.Equal(Prefix, AgentAccessKeyBootstrap.ResolvePrefix(Prefix, "https://hub.urgent.deliverdifferent.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://hub.urgent.example.test")]
    [InlineData("https://hub.local.deliverdifferent.com")]
    [InlineData("http://hub.urgent.deliverdifferent.com")]
    public void No_prefix_without_a_configured_one_or_a_tenant_issuer(string? issuer)
    {
        Assert.Null(AgentAccessKeyBootstrap.ResolvePrefix(null, issuer));
    }

    [Fact]
    public void A_configured_issuer_wins_over_the_tenant_url()
    {
        var (issuer, source) = AgentAccessKeyBootstrap.ResolveIssuer(
            " https://hub.urgent.deliverdifferent.com ", "https://app_name.other.deliverdifferent.com");

        Assert.Equal("https://hub.urgent.deliverdifferent.com", issuer);
        Assert.Equal(AgentAccessIssuerSource.Configured, source);
    }

    [Theory]
    [InlineData("https://app_name.urgent.deliverdifferent.com", "https://hub.urgent.deliverdifferent.com")]
    [InlineData("https://app_name.urgent.staging.deliverdifferent.com", "https://hub.urgent.staging.deliverdifferent.com")]
    [InlineData("https://app_name.staging.deliverdifferent.com", "https://hub.staging.deliverdifferent.com")]
    [InlineData(" https://app_name.urgent.deliverdifferent.com/ ", "https://hub.urgent.deliverdifferent.com")]
    public void Without_one_the_issuer_is_this_tenants_hub_from_the_tenant_url(string tenantUrl, string expected)
    {
        var (issuer, source) = AgentAccessKeyBootstrap.ResolveIssuer(null, tenantUrl);

        Assert.Equal(expected, issuer);
        Assert.Equal(AgentAccessIssuerSource.TenantUrl, source);
        Assert.NotNull(AgentAccessKeyBootstrap.ResolvePrefix(null, issuer));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://booking.urgent.deliverdifferent.com")]
    [InlineData("http://app_name.urgent.deliverdifferent.com")]
    [InlineData("https://app_name.local.deliverdifferent.com")]
    [InlineData("https://app_name.dev.deliverdifferent.com")]
    [InlineData("https://app_name.urgent.example.test")]
    [InlineData("https://app_name.urgent.notdeliverdifferent.com")]
    [InlineData("https://x.app_name.urgent.deliverdifferent.com")]
    [InlineData("https://app_name.urgent.deliverdifferent.com:8443")]
    [InlineData("not a url app_name")]
    public void A_tenant_url_that_names_no_tenant_hub_leaves_the_issuer_unset(string? tenantUrl)
    {
        var (issuer, source) = AgentAccessKeyBootstrap.ResolveIssuer("  ", tenantUrl);

        Assert.Null(issuer);
        Assert.Equal(AgentAccessIssuerSource.None, source);
    }

    // ---- helpers ----------------------------------------------------------

    // Stands in for SSM: reads see what earlier writes stored, each with the date it was written. A stale
    // first read stands for a pod that read the prefix before another pod's write landed.
    private static IAgentAccessKeySource Store(
        Dictionary<string, string> parameters,
        Dictionary<string, DateTime>? dates = null,
        DateTime? writtenAt = null,
        bool staleFirstRead = false)
    {
        dates ??= new Dictionary<string, DateTime>();
        var reads = 0;
        var source = Substitute.For<IAgentAccessKeySource>();
        source.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyDictionary<string, AgentAccessKeyParameter>)(staleFirstRead && reads++ == 0
                ? new Dictionary<string, AgentAccessKeyParameter>()
                : AgentAccessKeyLoaderTests.Stored(parameters, dates)));
        source.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                parameters.Add(call.ArgAt<string>(1), call.ArgAt<string>(2));
                dates[call.ArgAt<string>(1)] = writtenAt ?? AgentAccessKeyLoaderTests.Written;
                return Task.CompletedTask;
            });
        return source;
    }

    private static AgentAccessOAuthSettings Settings(IAgentAccessKeySource source, string? prefix = Prefix, string? issuer = Issuer) =>
        new(prefix, issuer, source, KeyWriteSettle: TimeSpan.Zero);

    private static IAgentAccessKeySource Source(Dictionary<string, string>? parameters = null)
    {
        var source = Substitute.For<IAgentAccessKeySource>();
        IReadOnlyDictionary<string, AgentAccessKeyParameter> value = AgentAccessKeyLoaderTests.Stored(parameters
            ?? AgentAccessKeyLoaderTests.Parameters(AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem()));
        source.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(value);
        return source;
    }
}
