using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Oauth;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Gate;

// Phase 1.2: the OAuth endpoints exist in the pipeline but answer 404 unless the agent
// access gate is open for the tenant. Requests that are not OAuth paths must pass through
// untouched and must not even consult the gate.
public class AgentAccessEndpointGateTests
{
    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/token")]
    [InlineData("/connect/anything/else")]
    [InlineData("/CONNECT/Token")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/.well-known/jwks")]
    [InlineData("/.well-known/JWKS")]
    public async Task Agent_access_paths_are_404_when_the_gate_is_closed(string path)
    {
        var (status, nextCalled, gate) = await Run(path, Decision(false));

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.False(nextCalled);
        await gate.Received(1).EvaluateAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/.well-known/jwks")]
    public async Task Agent_access_paths_pass_through_when_the_gate_is_open(string path)
    {
        var (status, nextCalled, _) = await Run(path, Decision(true));

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/connectx")]                       // not the /connect segment
    [InlineData("/api/connect/token")]
    [InlineData("/.well-known/security.txt")]
    [InlineData("/.well-known/openid-configuration-extra")]
    [InlineData("/health/live")]
    public async Task Other_paths_pass_through_without_consulting_the_gate(string path)
    {
        var (status, nextCalled, gate) = await Run(path, Decision(false));

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
        await gate.DidNotReceive().EvaluateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_throwing_gate_fails_closed_with_404()
    {
        var gate = Substitute.For<IAgentAccessGate>();
        gate.EvaluateAsync(Arg.Any<CancellationToken>())
            .Returns<AgentAccessDecision>(_ => throw new InvalidOperationException("boom"));

        var (status, nextCalled) = await Invoke("/connect/token", gate);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var gate = Substitute.For<IAgentAccessGate>();
        gate.EvaluateAsync(Arg.Any<CancellationToken>())
            .Returns<AgentAccessDecision>(_ => throw new OperationCanceledException(cts.Token));

        var context = new DefaultHttpContext { RequestAborted = cts.Token };
        context.Request.Path = "/connect/token";
        var middleware = new AgentAccessEndpointGate(_ => Task.CompletedTask);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(context, gate, Substitute.For<IAgentAccessClientSeeder>()));
    }

    // ---- client seeding (phase 1.4): only after the gate has opened ----------

    private static IAgentAccessGate GateWith(bool enabled)
    {
        var gate = Substitute.For<IAgentAccessGate>();
        gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(Decision(enabled));
        return gate;
    }

    [Fact]
    public async Task The_seeder_runs_when_the_gate_is_open_for_an_oauth_path()
    {
        var seeder = Substitute.For<IAgentAccessClientSeeder>();

        var (status, nextCalled) = await Invoke("/connect/token", GateWith(true), seeder);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
        await seeder.Received(1).EnsureAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/jwks")]
    public async Task The_seeder_never_runs_while_the_gate_is_closed(string path)
    {
        var seeder = Substitute.For<IAgentAccessClientSeeder>();

        await Invoke(path, GateWith(false), seeder);

        await seeder.DidNotReceive().EnsureAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    public async Task The_seeder_never_runs_for_other_paths(string path)
    {
        var seeder = Substitute.For<IAgentAccessClientSeeder>();

        await Invoke(path, GateWith(true), seeder);

        await seeder.DidNotReceive().EnsureAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failing_seeder_does_not_block_an_open_gate()
    {
        var seeder = Substitute.For<IAgentAccessClientSeeder>();
        seeder.EnsureAsync(Arg.Any<CancellationToken>()).Returns(_ => throw new InvalidOperationException("boom"));

        var (status, nextCalled) = await Invoke("/connect/token", GateWith(true), seeder);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
    }

    // ---- helpers ----------------------------------------------------------

    private static AgentAccessDecision Decision(bool enabled) =>
        new(enabled, enabled ? AgentAccessReason.Enabled : AgentAccessReason.TenantSwitchOff);

    private static async Task<(int Status, bool NextCalled, IAgentAccessGate Gate)> Run(
        string path, AgentAccessDecision decision)
    {
        var gate = Substitute.For<IAgentAccessGate>();
        gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(decision);
        var (status, nextCalled) = await Invoke(path, gate);
        return (status, nextCalled, gate);
    }

    private static async Task<(int Status, bool NextCalled)> Invoke(
        string path, IAgentAccessGate gate, IAgentAccessClientSeeder? seeder = null)
    {
        var nextCalled = false;
        var middleware = new AgentAccessEndpointGate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context, gate, seeder ?? Substitute.For<IAgentAccessClientSeeder>());

        return (context.Response.StatusCode, nextCalled);
    }
}
