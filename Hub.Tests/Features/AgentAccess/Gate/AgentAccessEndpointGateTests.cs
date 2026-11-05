using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Oauth;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Gate;

// Phase 1.2: the OAuth endpoints exist in the pipeline but answer 404 unless the agent
// access gate is open for the tenant. Requests that are not OAuth paths must pass through
// untouched and must not even consult the gate. A tenant that has switched agent access off
// (rather than a Hub where it is dark) still answers discovery, so Claude opens a browser and
// the person is told why.
public class AgentAccessEndpointGateTests
{
    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/token")]
    [InlineData("/connect/anything/else")]
    [InlineData("/CONNECT/Token")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/.well-known/oauth-authorization-server")]
    [InlineData("/.well-known/jwks")]
    [InlineData("/.well-known/JWKS")]
    public async Task Agent_access_paths_are_404_when_agent_access_is_dark(string path)
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
    [InlineData("/.well-known/oauth-authorization-server")]
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
            () => middleware.InvokeAsync(context, Tenants(), gate, Substitute.For<IAgentAccessClientSeeder>()));
    }

    // ---- which tenant: worked out before anyone signs in ---------------------

    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/.well-known/openid-configuration")]
    public async Task An_unknown_tenant_is_404_without_consulting_the_gate(string path)
    {
        var gate = GateWith(true);

        var (status, nextCalled) = await Invoke(path, gate, tenants: NoTenant());

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.False(nextCalled);
        await gate.DidNotReceive().EvaluateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_throwing_tenant_lookup_fails_closed_with_404()
    {
        var tenants = Substitute.For<IAgentAccessTenantResolver>();
        tenants.ResolveAsync(Arg.Any<CancellationToken>()).Returns<AgentAccessTenant?>(_ => throw new InvalidOperationException("master down"));

        var (status, nextCalled) = await Invoke("/connect/token", GateWith(true), tenants: tenants);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task The_tenant_is_resolved_before_the_gate_reads_its_settings()
    {
        var tenants = Tenants();
        var gate = GateWith(true);

        await Invoke("/connect/token", gate, tenants: tenants);

        Received.InOrder(() =>
        {
            tenants.ResolveAsync(Arg.Any<CancellationToken>());
            gate.EvaluateAsync(Arg.Any<CancellationToken>());
        });
    }

    // ---- switched off for this tenant: say so instead of hiding ------------

    [Theory]
    [InlineData(AgentAccessReason.TenantSwitchOff)]
    [InlineData(AgentAccessReason.ForcedOff)]
    public async Task A_switched_off_tenant_still_serves_discovery(AgentAccessReason reason)
    {
        foreach (var path in new[] { "/.well-known/openid-configuration", "/.well-known/oauth-authorization-server", "/.well-known/jwks" })
        {
            var (status, nextCalled) = await Invoke(path, GateWith(reason));

            Assert.Equal(StatusCodes.Status200OK, status);
            Assert.True(nextCalled, path);
        }
    }

    [Fact]
    public async Task A_switched_off_tenant_sends_authorize_to_the_unavailable_page()
    {
        var context = await InvokeContext("/connect/authorize", GateWith(AgentAccessReason.TenantSwitchOff));

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/agent-access/unavailable", context.Response.Headers.Location.ToString());
        Assert.False(context.Items.ContainsKey("next"));
    }

    [Fact]
    public async Task A_switched_off_tenant_refuses_the_token_endpoint_with_an_oauth_error()
    {
        var context = await InvokeContext("/connect/token", GateWith(AgentAccessReason.TenantSwitchOff));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(context.Items.ContainsKey("next"));
        context.Response.Body.Position = 0;
        using var json = System.Text.Json.JsonDocument.Parse(context.Response.Body);
        Assert.Equal("invalid_grant", json.RootElement.GetProperty("error").GetString());
        Assert.Contains("Urgent Couriers", json.RootElement.GetProperty("error_description").GetString());
    }

    [Fact]
    public async Task A_switched_off_tenant_404s_other_connect_paths()
    {
        var (status, nextCalled) = await Invoke("/connect/anything/else", GateWith(AgentAccessReason.TenantSwitchOff));

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.False(nextCalled);
    }

    [Theory]
    [InlineData(AgentAccessReason.KeysNotPersistent)]
    [InlineData(AgentAccessReason.Error)]
    public async Task Dark_reasons_hide_even_discovery_and_authorize(AgentAccessReason reason)
    {
        foreach (var path in new[] { "/.well-known/openid-configuration", "/connect/authorize", "/connect/token" })
        {
            var (status, nextCalled) = await Invoke(path, GateWith(reason));

            Assert.Equal(StatusCodes.Status404NotFound, status);
            Assert.False(nextCalled, path);
        }
    }

    [Theory]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/connect/authorize")]
    public async Task The_seeder_never_runs_for_a_switched_off_tenant(string path)
    {
        var seeder = Substitute.For<IAgentAccessClientSeeder>();

        await Invoke(path, GateWith(AgentAccessReason.TenantSwitchOff), seeder);

        await seeder.DidNotReceive().EnsureAsync(Arg.Any<CancellationToken>());
    }

    // ---- client seeding (phase 1.4): only after the gate has opened ----------

    private static IAgentAccessGate GateWith(bool enabled) => GateWith(Decision(enabled));

    private static IAgentAccessGate GateWith(AgentAccessReason reason) =>
        GateWith(new AgentAccessDecision(reason == AgentAccessReason.Enabled, reason));

    private static IAgentAccessGate GateWith(AgentAccessDecision decision)
    {
        var gate = Substitute.For<IAgentAccessGate>();
        gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(decision);
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
        new(enabled, enabled ? AgentAccessReason.Enabled : AgentAccessReason.Error);

    private static IAgentAccessTenantResolver Tenants()
    {
        var tenants = Substitute.For<IAgentAccessTenantResolver>();
        tenants.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessTenant(12, "urgent", "Urgent Couriers"));
        return tenants;
    }

    private static IAgentAccessTenantResolver NoTenant()
    {
        var tenants = Substitute.For<IAgentAccessTenantResolver>();
        tenants.ResolveAsync(Arg.Any<CancellationToken>()).Returns((AgentAccessTenant?)null);
        return tenants;
    }

    private static async Task<(int Status, bool NextCalled, IAgentAccessGate Gate)> Run(
        string path, AgentAccessDecision decision)
    {
        var gate = Substitute.For<IAgentAccessGate>();
        gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(decision);
        var (status, nextCalled) = await Invoke(path, gate);
        return (status, nextCalled, gate);
    }

    private static async Task<(int Status, bool NextCalled)> Invoke(
        string path, IAgentAccessGate gate, IAgentAccessClientSeeder? seeder = null, IAgentAccessTenantResolver? tenants = null)
    {
        var context = await InvokeContext(path, gate, seeder, tenants);
        return (context.Response.StatusCode, context.Items.ContainsKey("next"));
    }

    private static async Task<HttpContext> InvokeContext(
        string path, IAgentAccessGate gate, IAgentAccessClientSeeder? seeder = null, IAgentAccessTenantResolver? tenants = null)
    {
        var middleware = new AgentAccessEndpointGate(c =>
        {
            c.Items["next"] = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, tenants ?? Tenants(), gate, seeder ?? Substitute.For<IAgentAccessClientSeeder>());

        return context;
    }
}
