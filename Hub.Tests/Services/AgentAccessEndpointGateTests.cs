using Hub.Interfaces;
using Hub.Services;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Hub.Tests.Services;

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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => middleware.InvokeAsync(context, gate));
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

    private static async Task<(int Status, bool NextCalled)> Invoke(string path, IAgentAccessGate gate)
    {
        var nextCalled = false;
        var middleware = new AgentAccessEndpointGate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context, gate);

        return (context.Response.StatusCode, nextCalled);
    }
}
