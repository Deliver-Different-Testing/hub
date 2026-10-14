using System.Net;
using Hub.Extensions;
using Hub.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Hub.Tests.Extensions;

// Phase 1.2 end to end, on a real ASP.NET Core pipeline: OpenIddict registered exactly as
// Hub registers it, the gate placed before authentication. Closed gate = the OAuth server
// does not exist; open gate = it answers.
public class AgentAccessPipelineTests
{
    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/.well-known/jwks")]
    public async Task Closed_gate_makes_every_oauth_path_404(string path)
    {
        await using var app = await StartAsync(gateOpen: false);

        var response = await Client(app).GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Closed_gate_leaves_ordinary_requests_untouched()
    {
        await using var app = await StartAsync(gateOpen: false);

        var response = await Client(app).GetAsync("/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Open_gate_serves_the_discovery_document_with_pkce_S256_only()
    {
        await using var app = await StartAsync(gateOpen: true);

        var response = await Client(app)
            .GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"authorization_endpoint\"", body);
        Assert.Contains("\"token_endpoint\"", body);
        Assert.Contains("\"S256\"", body);
        Assert.DoesNotContain("\"plain\"", body);
    }

    [Fact]
    public async Task Open_gate_serves_the_jwks()
    {
        await using var app = await StartAsync(gateOpen: true);

        var response = await Client(app)
            .GetAsync("/.well-known/jwks", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"keys\"", body);
    }

    // ---- helpers ----------------------------------------------------------

    // OpenIddict refuses plain HTTP, and GetTestClient returns a fresh client on every call.
    private static HttpClient Client(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    private static async Task<WebApplication> StartAsync(bool gateOpen)
    {
        var gate = Substitute.For<IAgentAccessGate>();
        gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessDecision(
            gateOpen, gateOpen ? AgentAccessReason.Enabled : AgentAccessReason.TenantSwitchOff));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(gate);
        builder.Services.AddAuthentication("Cookies").AddCookie("Cookies");
        builder.Services.AddAuthorization();
        builder.Services.AddAgentAccessOAuth("Server=unused;Database=unused;");

        var app = builder.Build();
        app.UseRouting();
        app.UseAgentAccessEndpointGate();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/ping", () => "pong");

        await app.StartAsync();
        return app;
    }
}
