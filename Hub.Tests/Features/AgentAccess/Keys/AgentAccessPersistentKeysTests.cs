using Hub.Features.AgentAccess;
using Hub.Features.AgentAccess.Keys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenIddict.Server;

namespace Hub.Tests.Features.AgentAccess.Keys;

// Phase 1.3: OpenIddict runs on the persistent keys and issuer from settings, with the token
// options from the spec (v4 Group 1). Without settings it keeps ephemeral keys and the
// key state reports "not persistent" so the agent access gate stays closed.
public class AgentAccessPersistentKeysTests
{
    private const string Issuer = "https://hub.urgent.example.test";
    private const string Prefix = "/Hub/urgent/AgentAccess";

    [Fact]
    public void Persistent_keys_become_the_signing_and_encryption_credentials()
    {
        var parameters = AgentAccessKeyLoaderTests.Parameters(AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());
        var expected = AgentAccessKeyLoader.Load(parameters).Keys!;

        var options = ServerOptions(Settings(parameters));

        Assert.Equal(expected.Signing.KeyId, options.SigningCredentials[0].Key.KeyId);
        Assert.Equal(expected.Encryption.KeyId, options.EncryptionCredentials[0].Key.KeyId);
        Assert.Single(options.SigningCredentials);
    }

    [Fact]
    public void The_previous_signing_key_is_published_after_the_current_one()
    {
        var parameters = AgentAccessKeyLoaderTests.Parameters(AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());
        parameters[AgentAccessKeyLoader.PreviousSigningKeyName] = AgentAccessKeyLoaderTests.Pem();
        var expected = AgentAccessKeyLoader.Load(parameters).Keys!;

        var options = ServerOptions(Settings(parameters));

        // OpenIddict signs with the first credential, so the current key must come first.
        Assert.Equal(2, options.SigningCredentials.Count);
        Assert.Equal(expected.Signing.KeyId, options.SigningCredentials[0].Key.KeyId);
        Assert.Equal(expected.PreviousSigning!.KeyId, options.SigningCredentials[1].Key.KeyId);
    }

    [Fact]
    public void The_issuer_comes_from_settings()
    {
        var options = ServerOptions(Settings(DefaultParameters()));

        Assert.Equal(new Uri(Issuer), options.Issuer);
    }

    [Fact]
    public void Persistent_keys_report_persistent_with_the_signing_kid()
    {
        using var provider = Provider(Settings(DefaultParameters()));

        var state = provider.GetRequiredService<IAgentAccessKeyState>();

        Assert.True(state.Persistent);
        Assert.False(string.IsNullOrEmpty(state.SigningKeyId));
    }

    [Fact]
    public void Without_settings_the_key_state_is_not_persistent_and_ephemeral_keys_are_used()
    {
        using var provider = Provider(settings: null);

        var state = provider.GetRequiredService<IAgentAccessKeyState>();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;

        Assert.False(state.Persistent);
        Assert.NotEmpty(options.SigningCredentials);
        Assert.NotEmpty(options.EncryptionCredentials);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Token_options_apply_with_or_without_persistent_keys(bool persistent)
    {
        var options = ServerOptions(persistent ? Settings(DefaultParameters()) : null);

        Assert.Equal(TimeSpan.FromMinutes(20), options.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(90), options.RefreshTokenLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RefreshTokenReuseLeeway);
        Assert.True(options.UseReferenceRefreshTokens);
        Assert.True(options.DisableAccessTokenEncryption);
    }

    // ---- helpers ----------------------------------------------------------

    private static Dictionary<string, string> DefaultParameters() =>
        AgentAccessKeyLoaderTests.Parameters(AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());

    internal static AgentAccessOAuthSettings Settings(Dictionary<string, string> parameters)
    {
        var source = Substitute.For<IAgentAccessKeySource>();
        IReadOnlyDictionary<string, string> value = parameters;
        source.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(value);
        return new AgentAccessOAuthSettings(Prefix, Issuer, source);
    }

    private static OpenIddictServerOptions ServerOptions(AgentAccessOAuthSettings? settings)
    {
        using var provider = Provider(settings);
        return provider.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
    }

    private static ServiceProvider Provider(AgentAccessOAuthSettings? settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentAccessOAuth("Server=unused;Database=unused;", settings);
        return services.BuildServiceProvider();
    }
}
