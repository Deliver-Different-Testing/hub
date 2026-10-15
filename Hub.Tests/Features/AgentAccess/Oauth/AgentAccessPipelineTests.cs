using System.Net;
using System.Text.Json;
using Hub.Features.AgentAccess;
using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Keys;
using Hub.Tests.Features.AgentAccess.Keys;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Oauth;

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

    [Fact]
    public async Task Open_gate_publishes_the_current_and_previous_public_keys_only()
    {
        var parameters = AgentAccessKeyLoaderTests.Parameters(AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());
        parameters[AgentAccessKeyLoader.PreviousSigningKeyName] = AgentAccessKeyLoaderTests.Pem();
        var expected = AgentAccessKeyLoader.Load(parameters).Keys!;
        await using var app = await StartAsync(gateOpen: true, AgentAccessPersistentKeysTests.Settings(parameters));

        var body = await Client(app).GetStringAsync("/.well-known/jwks", TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(body);
        var keys = doc.RootElement.GetProperty("keys").EnumerateArray().ToList();
        var kids = keys.Select(k => k.GetProperty("kid").GetString()).ToArray();
        Assert.Equal(2, kids.Length);
        Assert.Contains(expected.Signing.KeyId, kids);
        Assert.Contains(expected.PreviousSigning!.KeyId, kids);
        Assert.DoesNotContain(keys, k => k.GetProperty("kid").GetString() == expected.Encryption.KeyId);
        foreach (var key in keys)
        {
            foreach (var privateMember in new[] { "d", "p", "q", "dp", "dq", "qi" })
            {
                Assert.False(key.TryGetProperty(privateMember, out _), $"JWKS leaked private member {privateMember}");
            }
        }
    }

    [Fact]
    public async Task Discovery_issuer_is_the_configured_issuer_not_the_request_host()
    {
        var parameters = AgentAccessKeyLoaderTests.Parameters(AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());
        await using var app = await StartAsync(gateOpen: true, AgentAccessPersistentKeysTests.Settings(parameters));

        var body = await Client(app).GetStringAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("https://hub.urgent.example.test/", doc.RootElement.GetProperty("issuer").GetString());
    }

    // ---- helpers ----------------------------------------------------------

    // OpenIddict refuses plain HTTP, and GetTestClient returns a fresh client on every call.
    private static HttpClient Client(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    private static async Task<WebApplication> StartAsync(bool gateOpen, AgentAccessOAuthSettings? settings = null)
    {
        var gate = Substitute.For<IAgentAccessGate>();
        gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessDecision(
            gateOpen, gateOpen ? AgentAccessReason.Enabled : AgentAccessReason.TenantSwitchOff));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(gate);
        builder.Services.AddAuthentication("Cookies").AddCookie("Cookies");
        builder.Services.AddAuthorization();
        builder.Services.AddAgentAccessOAuth("Server=unused;Database=unused;", settings);

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
