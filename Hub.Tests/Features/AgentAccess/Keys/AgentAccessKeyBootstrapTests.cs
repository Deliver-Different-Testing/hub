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
            .Returns<IReadOnlyDictionary<string, string>>(_ => throw new InvalidOperationException("AccessDenied secret-detail"));

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
                return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
            });

        var result = AgentAccessKeyBootstrap.Resolve(
            new AgentAccessOAuthSettings(Prefix, Issuer, source, LoadTimeout: TimeSpan.FromMilliseconds(150)));

        Assert.False(result.State.Persistent);
        Assert.Contains("timed out", result.State.Reason);
    }

    // ---- helpers ----------------------------------------------------------

    private static IAgentAccessKeySource Source(Dictionary<string, string>? parameters = null)
    {
        var source = Substitute.For<IAgentAccessKeySource>();
        IReadOnlyDictionary<string, string> value = parameters
            ?? AgentAccessKeyLoaderTests.Parameters(AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());
        source.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(value);
        return source;
    }
}
