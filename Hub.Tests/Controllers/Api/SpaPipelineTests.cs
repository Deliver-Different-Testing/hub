using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Oauth;
using Hub.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Hub.Tests.Controllers.Api;

// The real Program pipeline: cookie auth events, antiforgery, MVC routes and the SPA fallback,
// with the data-hitting services replaced so nothing reaches a database.
public sealed class HubFactory : WebApplicationFactory<Program>
{
    public const string IndexMarker = "<!-- spa-index-placeholder -->";

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "hub-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _originalEnv = [];

    public IAccountService Accounts { get; } = Substitute.For<IAccountService>();

    public bool AgentGateOpen { get; set; }

    public HubFactory()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "static"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), $"<html><body>{IndexMarker}</body></html>");
        File.WriteAllText(Path.Combine(_webRoot, "sw.js"), "// sw");
        File.WriteAllText(Path.Combine(_webRoot, "static", "app-abc123.js"), "// app");

        SetEnv("MasterSQLConnection", "Server=localhost;Database=Master;User Id=x;Password=x;TrustServerCertificate=True");
        SetEnv("RedisConfig", "localhost:6379,abortConnect=false");
        SetEnv("ASPNETCORE_ENVIRONMENT", "Development");
    }

    private void SetEnv(string name, string value)
    {
        _originalEnv[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseWebRoot is ignored by the minimal-hosting builder once Program has created it, so the
        // placeholder shell is installed on the environment itself.
        builder.ConfigureServices((context, _) =>
        {
            context.HostingEnvironment.WebRootPath = _webRoot;
            context.HostingEnvironment.WebRootFileProvider = new PhysicalFileProvider(_webRoot);
        });
        builder.ConfigureTestServices(services =>
        {
            // Session state goes to Redis in production; an in-memory store keeps these tests off the network.
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            var gate = Substitute.For<IAgentAccessGate>();
            gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(_ => new AgentAccessDecision(
                AgentGateOpen, AgentGateOpen ? AgentAccessReason.Enabled : AgentAccessReason.ForcedOff));
            services.AddScoped(_ => gate);
            services.AddSingleton(Substitute.For<IAgentAccessClientSeeder>());
            services.AddScoped(_ => Accounts);
        });
    }

    public HttpClient CreateSpaClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var (name, value) in _originalEnv)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        try
        {
            Directory.Delete(_webRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

[Collection("EnvironmentVariables")]
public sealed class SpaPipelineTests : IClassFixture<HubFactory>
{
    private readonly HubFactory _factory;

    public SpaPipelineTests(HubFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync(Ct);

    private static bool IsSpaShell(string body) => body.Contains(HubFactory.IndexMarker);

    // ---- 401 not 302 ----

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/me/settings")]
    [InlineData("/api/home")]
    [InlineData("/api/fuel-surcharge")]
    public async Task Unauthenticated_api_get_is_401_not_a_login_redirect(string path)
    {
        var client = _factory.CreateSpaClient();

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Unauthenticated_api_401_carries_the_contract_error_json()
    {
        // An empty body with no content type is what Firefox reports as "XML Parsing Error: no root element found".
        var client = _factory.CreateSpaClient();

        var response = await client.GetAsync("/api/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("""{"error":"Not signed in."}""", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Unauthenticated_api_mutation_with_antiforgery_is_401_not_a_login_redirect()
    {
        var client = _factory.CreateSpaClient();
        await AttachAntiforgeryAsync(client);

        var response = await client.PostAsync("/api/me/api-key", null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    // ---- antiforgery ----

    [Fact]
    public async Task Antiforgery_endpoint_sets_a_script_readable_strict_cookie()
    {
        var client = _factory.CreateSpaClient();

        var response = await client.GetAsync("/api/antiforgery", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        // Secure is environment-dependent (this factory runs as Development); see AntiforgeryApiControllerTests.
    }

    [Fact]
    public async Task Login_post_without_an_antiforgery_token_is_400()
    {
        var client = _factory.CreateSpaClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = "staff@test.com", password = "x" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Accounts.DidNotReceiveWithAnyArgs().LoginAsync(default!, default!, default);
    }

    [Fact]
    public async Task Login_post_with_the_antiforgery_pair_reaches_the_service_and_answers_camelCase_json()
    {
        var client = _factory.CreateSpaClient();
        _factory.Accounts.LoginAsync(default!, default!, default)
            .ReturnsForAnyArgs(Hub.Interfaces.AuthResult.Redirect("/connect/authorize?x=1"));
        await AttachAntiforgeryAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "staff@test.com", password = "TestPassword1!", rememberMe = false, isCourierLogin = false, returnUrl = "/connect/authorize?x=1" },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await BodyAsync(response));
        Assert.Equal("/connect/authorize?x=1", doc.RootElement.GetProperty("redirectUrl").GetString());
        await _factory.Accounts.Received().LoginAsync(
            Arg.Any<Microsoft.AspNetCore.Http.HttpContext>(), Arg.Any<Hub.ViewModels.LoginViewModel>(),
            "/connect/authorize?x=1");
    }

    [Fact]
    public async Task Login_post_failure_answers_400_with_an_error_body()
    {
        var client = _factory.CreateSpaClient();
        _factory.Accounts.LoginAsync(default!, default!, default)
            .ReturnsForAnyArgs(Hub.Interfaces.AuthResult.Failure("Invalid login attempt."));
        await AttachAntiforgeryAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = "staff@test.com", password = "wrong" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await BodyAsync(response));
        Assert.Equal("Invalid login attempt.", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Login_post_with_an_invalid_body_answers_400_with_an_error_body_not_problem_details()
    {
        var client = _factory.CreateSpaClient();
        await AttachAntiforgeryAsync(client);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "not-an-email", password = "x" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await BodyAsync(response));
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Config_is_anonymous()
    {
        var client = _factory.CreateSpaClient();

        var response = await client.GetAsync("/api/config", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await BodyAsync(response));
        Assert.True(doc.RootElement.TryGetProperty("recaptchaSiteKey", out _));
    }

    // ---- SPA fallback ----

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/Account/ResetPassword")]
    [InlineData("/Account/Settings")]
    [InlineData("/FuelSurcharge")]
    public async Task Spa_routes_serve_the_index_shell(string path)
    {
        var client = _factory.CreateSpaClient();

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var shell = await BodyAsync(response);
        Assert.True(IsSpaShell(shell), $"{response.Content.Headers.ContentType} :: {shell}");
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Unknown_api_path_is_404_not_the_spa_shell()
    {
        var client = _factory.CreateSpaClient();

        var response = await client.GetAsync("/api/unknown", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(IsSpaShell(await BodyAsync(response)));
    }

    [Fact]
    public async Task Fallback_does_not_swallow_the_tenant_switch_route()
    {
        var client = _factory.CreateSpaClient();

        var response = await client.GetAsync("/Account/AcceptTenantSwitchToken", Ct);

        // The MVC action redirects a missing token to the SPA login; the shell itself is not served here.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
        await _factory.Accounts.Received().AcceptTenantSwitchTokenAsync(
            Arg.Any<Microsoft.AspNetCore.Http.HttpContext>(), null);
    }

    [Fact]
    public async Task Fallback_does_not_swallow_connect_authorize_while_the_gate_is_closed()
    {
        _factory.AgentGateOpen = false;
        var client = _factory.CreateSpaClient();

        var response = await client.GetAsync("/connect/authorize", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(IsSpaShell(await BodyAsync(response)));
    }

    [Fact]
    public async Task Fallback_does_not_swallow_connect_authorize_when_the_gate_is_open()
    {
        using var factory = new HubFactory { AgentGateOpen = true };
        var client = factory.CreateSpaClient();

        var response = await client.GetAsync("/connect/authorize", Ct);

        // OpenIddict answers (an invalid_request without a client_id); the SPA shell does not.
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.False(IsSpaShell(await BodyAsync(response)));
    }

    // ---- static file caching ----

    [Theory]
    [InlineData("/index.html")]
    [InlineData("/sw.js")]
    public async Task Shell_files_are_no_cache(string path)
    {
        var client = _factory.CreateSpaClient();

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        if (path == "/index.html")
        {
            Assert.True(IsSpaShell(await BodyAsync(response)), "static index.html is not the placeholder");
        }
    }

    [Fact]
    public async Task Hashed_static_assets_are_cached_immutably()
    {
        var client = _factory.CreateSpaClient();

        var response = await client.GetAsync("/static/app-abc123.js", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    // ---- helpers ----

    private static async Task AttachAntiforgeryAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/antiforgery", Ct);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        var token = Uri.UnescapeDataString(cookie["XSRF-TOKEN=".Length..].Split(';')[0]);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
    }
}
