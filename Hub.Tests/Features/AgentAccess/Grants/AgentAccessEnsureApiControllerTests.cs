using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Grants;
using Hub.Features.AgentAccess.Keys;
using Hub.Features.AgentAccess.Oauth;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Grants;

[Collection("PartnerDirectoryApiKey")]
public class AgentAccessEnsureApiControllerTests : IDisposable
{
    private const string Key = "shared-key";
    private readonly string? _original = Environment.GetEnvironmentVariable("PartnerDirectoryApiKey");
    private readonly IAgentAccessTenantResolver _tenants = Substitute.For<IAgentAccessTenantResolver>();
    private readonly IAgentAccessGate _gate = Substitute.For<IAgentAccessGate>();
    private readonly IAgentAccessClientSeeder _seeder = Substitute.For<IAgentAccessClientSeeder>();
    private IAgentAccessKeyState _keys = new AgentAccessKeyState(true, "persistent", "kid");
    private AgentAccessIssuer _issuer = new(new Uri("https://hub.acme.example"));

    public AgentAccessEnsureApiControllerTests()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", Key);
        _tenants.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessTenant(12, "acme", "Acme"));
        _gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessDecision(true, AgentAccessReason.Enabled));
        _seeder.Seeded.Returns(true);
        _seeder.TokenExchangeConfigured.Returns(true);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", _original);
        GC.SuppressFinalize(this);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AgentAccessEnsureApiController Sut() => new(_tenants, _gate, _seeder, _keys, _issuer);

    private async Task<AgentAccessEnsureResult> Ensure()
    {
        var ok = Assert.IsType<OkObjectResult>(await Sut().Ensure(12, Key, Ct));
        return Assert.IsType<AgentAccessEnsureResult>(ok.Value);
    }

    private static AgentAccessEnsureCheck Check(AgentAccessEnsureResult result, string key) => result.Checks.Single(c => c.Key == key);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task A_missing_or_wrong_key_is_refused_and_nothing_is_read_or_seeded(string? key)
    {
        Assert.IsType<UnauthorizedResult>(await Sut().Ensure(12, key, Ct));

        await _tenants.DidNotReceiveWithAnyArgs().ResolveAsync(default);
        await _seeder.DidNotReceiveWithAnyArgs().EnsureAsync(default);
    }

    [Fact]
    public async Task A_tenant_that_is_not_this_hubs_is_not_found()
    {
        Assert.IsType<NotFoundResult>(await Sut().Ensure(99, Key, Ct));
        await _seeder.DidNotReceiveWithAnyArgs().EnsureAsync(default);
    }

    [Fact]
    public async Task An_unresolved_tenant_is_not_found()
    {
        _tenants.ResolveAsync(Arg.Any<CancellationToken>()).Returns((AgentAccessTenant?)null);

        Assert.IsType<NotFoundResult>(await Sut().Ensure(12, Key, Ct));
        await _seeder.DidNotReceiveWithAnyArgs().EnsureAsync(default);
    }

    [Fact]
    public async Task A_closed_gate_is_not_ready_and_seeds_nothing()
    {
        _gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessDecision(false, AgentAccessReason.TenantSwitchOff));
        _seeder.Seeded.Returns(false);

        var result = await Ensure();

        Assert.False(result.Ready);
        Assert.Equal("TenantSwitchOff", result.Reason);
        Assert.False(Check(result, "switch").Ok);
        Assert.False(Check(result, "clients").Ok);
        await _seeder.DidNotReceiveWithAnyArgs().EnsureAsync(default);
    }

    [Fact]
    public async Task An_open_gate_seeds_once_and_is_ready_when_every_check_passes()
    {
        var result = await Ensure();

        Assert.True(result.Ready);
        Assert.Equal("Enabled", result.Reason);
        Assert.Equal(["keys", "issuer", "switch", "clients", "mcpExchange"], result.Checks.Select(c => c.Key).ToArray());
        Assert.All(result.Checks, c => Assert.True(c.Ok));
        await _seeder.Received(1).EnsureAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Keys_that_are_not_persistent_fail_the_keys_check()
    {
        _keys = new AgentAccessKeyState(false, "reading the keys from SSM failed (/secret/path)", null);
        _gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessDecision(false, AgentAccessReason.KeysNotPersistent));

        var result = await Ensure();

        Assert.False(result.Ready);
        Assert.False(Check(result, "keys").Ok);
        Assert.DoesNotContain("secret", Check(result, "keys").Detail);
    }

    [Fact]
    public async Task A_missing_issuer_fails_the_issuer_check()
    {
        _issuer = new AgentAccessIssuer(null);

        var result = await Ensure();

        Assert.False(result.Ready);
        Assert.False(Check(result, "issuer").Ok);
    }

    [Fact]
    public async Task An_unseeded_client_store_is_not_ready()
    {
        _seeder.Seeded.Returns(false);

        var result = await Ensure();

        Assert.False(result.Ready);
        Assert.False(Check(result, "clients").Ok);
    }

    [Fact]
    public async Task A_missing_mcp_exchange_secret_fails_the_mcp_exchange_check()
    {
        _seeder.TokenExchangeConfigured.Returns(false);

        var result = await Ensure();

        Assert.False(result.Ready);
        Assert.False(Check(result, "mcpExchange").Ok);
        Assert.True(Check(result, "clients").Ok);
    }

    [Fact]
    public async Task Calling_twice_gives_the_same_answer()
    {
        var first = await Ensure();
        var second = await Ensure();

        Assert.Equal(first.Ready, second.Ready);
        Assert.Equal(first.Reason, second.Reason);
        Assert.Equal(first.Checks, second.Checks);
    }
}
