using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hub.Controllers;
using Hub.Extensions;
using Hub.Interfaces;
using Hub.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Hub.Tests.Extensions;
using Hub.Features.AgentAccess;

namespace Hub.Tests.Features.AgentAccess;

// Phase 1.5 end to end on a real ASP.NET Core pipeline: OpenIddict registered exactly as Hub registers it,
// the open gate, a stand-in for the Hub cookie, the real controllers and the consent view.
public partial class AgentAccessAuthorizePipelineTests
{
    private const string Issuer = "https://hub.urgent.example.test";
    private const string Resource = "https://mcp.urgent.example.test/mcp";
    private const string RedirectUri = "https://claude.ai/api/mcp/auth_callback";
    private const string Verifier = "a-code-verifier-that-is-long-enough-to-satisfy-the-pkce-rules-1234567890";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Challenge() =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string AuthorizeUrl(
        string? resource = Resource, string scope = "mcp offline_access", string? prompt = null, string clientId = "dfrnt-claude")
    {
        var query = new List<string>
        {
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(RedirectUri)}",
            "response_type=code",
            $"scope={Uri.EscapeDataString(scope)}",
            "state=xyz",
            $"code_challenge={Challenge()}",
            "code_challenge_method=S256"
        };
        if (resource is not null) query.Add($"resource={Uri.EscapeDataString(resource)}");
        if (prompt is not null) query.Add($"prompt={prompt}");
        return "/connect/authorize?" + string.Join('&', query);
    }

    // ---- sign-in, validation --------------------------------------------

    [Fact]
    public async Task Signed_out_users_are_sent_to_login_and_return_to_the_same_request()
    {
        await using var app = await StartAsync();
        var url = AuthorizeUrl();

        var response = await Get(app, url, user: null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/Account/Login?ReturnUrl=", location);
        Assert.Equal(url, Uri.UnescapeDataString(location["/Account/Login?ReturnUrl=".Length..]));
    }

    [Fact]
    public async Task Prompt_none_when_signed_out_returns_login_required_to_the_client()
    {
        await using var app = await StartAsync();

        var response = await Get(app, AuthorizeUrl(prompt: "none"), user: null);

        AssertErrorRedirect(response, "login_required");
    }

    [Theory]
    [InlineData("https://mcp.other.example.test/mcp")]
    [InlineData(null)]
    public async Task Wrong_or_missing_resource_is_invalid_target(string? resource)
    {
        await using var app = await StartAsync();

        var response = await Get(app, AuthorizeUrl(resource: resource), Staff());

        AssertErrorRedirect(response, "invalid_target");
    }

    [Fact]
    public async Task Scopes_outside_the_clients_permissions_are_refused_before_a_code_is_issued()
    {
        await using var app = await StartAsync();

        var response = await Get(app, AuthorizeUrl(scope: "mcp admin"), Staff());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("code=", response.Headers.Location?.OriginalString ?? string.Empty);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("6")]
    [InlineData(null)]
    public async Task Customers_connected_tenants_and_couriers_are_denied(string? clientType)
    {
        await using var app = await StartAsync();

        var response = await Get(app, AuthorizeUrl(), Staff(clientType));

        AssertErrorRedirect(response, "access_denied");
    }

    // ---- consent ----------------------------------------------------------

    [Theory]
    [InlineData("4")]
    [InlineData("5")]
    public async Task Tenant_staff_and_df_admin_see_the_consent_page(string clientType)
    {
        await using var app = await StartAsync();

        var response = await Get(app, AuthorizeUrl(), Staff(clientType));
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Claude", html);
        Assert.Contains("Anthropic", html);
        Assert.Contains("mcp.urgent.example.test", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Allow_creates_a_permanent_authorization_with_a_numeric_tenant_id_and_returns_a_code()
    {
        await using var app = await StartAsync();

        var response = await Consent(app, AuthorizeUrl(), "allow");

        var query = AssertRedirectToClient(response);
        Assert.False(string.IsNullOrEmpty(query["code"]));
        Assert.Equal("xyz", query["state"]);

        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
        var authorization = Assert.Single(ctx.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().ToList());
        Assert.Equal("permanent", authorization.Type);
        Assert.Equal("valid", authorization.Status);
        Assert.Equal("55", authorization.Subject);
        using var props = JsonDocument.Parse(authorization.Properties!);
        Assert.Equal(JsonValueKind.Number, props.RootElement.GetProperty("tenant_id").ValueKind);
        Assert.Equal(12, props.RootElement.GetProperty("tenant_id").GetInt32());
    }

    [Fact]
    public async Task The_code_redeems_for_tokens_bound_to_this_tenants_mcp_resource()
    {
        await using var app = await StartAsync();
        var code = AssertRedirectToClient(await Consent(app, AuthorizeUrl(), "allow"))["code"]!;

        var response = await Token(app, code, Verifier);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.GetProperty("access_token").GetString());
        Assert.Contains(Resource, jwt.Audiences);
        Assert.Equal("55", jwt.Subject);
        Assert.Equal("12", jwt.Claims.Single(c => c.Type == "tenant_id").Value);
        Assert.Contains("mcp", jwt.Claims.Where(c => c.Type == "scope").SelectMany(c => c.Value.Split(' ')));
        Assert.True(body.TryGetProperty("refresh_token", out _));
        Assert.DoesNotContain(jwt.Claims, c => c.Type is "Connection" or "RememberMe" or "CourierID");
    }

    [Fact]
    public async Task A_wrong_code_verifier_is_refused()
    {
        await using var app = await StartAsync();
        var code = AssertRedirectToClient(await Consent(app, AuthorizeUrl(), "allow"))["code"]!;

        var response = await Token(app, code, Verifier + "x");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_grant", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Deny_returns_access_denied_and_creates_nothing()
    {
        await using var app = await StartAsync();

        var response = await Consent(app, AuthorizeUrl(), "deny");

        AssertErrorRedirect(response, "access_denied");
        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
        Assert.Empty(ctx.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>());
    }

    [Fact]
    public async Task An_existing_authorization_skips_consent_and_issues_a_code()
    {
        await using var app = await StartAsync();
        await Consent(app, AuthorizeUrl(), "allow");

        var response = await Get(app, AuthorizeUrl(), Staff());

        Assert.False(string.IsNullOrEmpty(AssertRedirectToClient(response)["code"]));
    }

    [Fact]
    public async Task Prompt_consent_forces_the_page_even_with_an_authorization()
    {
        await using var app = await StartAsync();
        await Consent(app, AuthorizeUrl(), "allow");

        var response = await Get(app, AuthorizeUrl(prompt: "consent"), Staff());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Prompt_none_without_an_authorization_is_consent_required()
    {
        await using var app = await StartAsync();

        var response = await Get(app, AuthorizeUrl(prompt: "none"), Staff());

        AssertErrorRedirect(response, "consent_required");
    }

    [Fact]
    public async Task An_authorization_from_another_tenant_does_not_skip_consent()
    {
        await using var app = await StartAsync();
        await Consent(app, AuthorizeUrl(), "allow", Staff(tenantId: "99"));

        var response = await Get(app, AuthorizeUrl(), Staff(tenantId: "12"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Posting_without_the_antiforgery_token_is_rejected_and_creates_nothing()
    {
        await using var app = await StartAsync();
        var page = await Get(app, AuthorizeUrl(), Staff());
        var fields = HiddenFields(await page.Content.ReadAsStringAsync(Ct));
        fields["decision"] = "allow";
        var request = new HttpRequestMessage(HttpMethod.Post, AuthorizeUrl())
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Add("X-Test-User", Staff());

        var response = await Client(app).SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
        Assert.Empty(ctx.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>());
    }

    [Fact]
    public async Task A_closed_gate_still_hides_both_endpoints()
    {
        await using var app = await StartAsync(gateOpen: false);

        Assert.Equal(HttpStatusCode.NotFound, (await Get(app, AuthorizeUrl(), Staff())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(app).PostAsync("/connect/token", new FormUrlEncodedContent([]), Ct)).StatusCode);
    }

    // ---- helpers ----------------------------------------------------------

    private static string Staff(string? clientType = "4", string tenantId = "12")
    {
        var parts = new List<string> { "UserID=55", "StaffID=7", "ContactID=9", $"CurrentTenantID={tenantId}" };
        if (clientType is not null) parts.Add($"ClientTypeId={clientType}");
        return string.Join(';', parts);
    }

    private static HttpClient Client(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    private static Task<HttpResponseMessage> Get(WebApplication app, string url, string? user)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (user is not null) request.Headers.Add("X-Test-User", user);
        return Client(app).SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> Consent(WebApplication app, string url, string decision, string? user = null)
    {
        user ??= Staff();
        var page = await Get(app, url, user);
        var html = await page.Content.ReadAsStringAsync(Ct);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var cookies = page.Headers.TryGetValues("Set-Cookie", out var set)
            ? string.Join("; ", set.Select(c => c.Split(';')[0]))
            : string.Empty;

        var fields = HiddenFields(html);
        fields["decision"] = decision;
        fields["__RequestVerificationToken"] = token;

        var post = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        post.Headers.Add("X-Test-User", user);
        if (cookies.Length > 0) post.Headers.Add("Cookie", cookies);
        return await Client(app).SendAsync(post, Ct);
    }

    private static Dictionary<string, string> HiddenFields(string html) =>
        Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\"")
            .Where(m => m.Groups[1].Value != "__RequestVerificationToken")
            .ToDictionary(m => WebUtility.HtmlDecode(m.Groups[1].Value), m => WebUtility.HtmlDecode(m.Groups[2].Value));

    private static Task<HttpResponseMessage> Token(WebApplication app, string code, string verifier) =>
        Client(app).PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "dfrnt-claude",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier
        }), Ct);

    private static void AssertErrorRedirect(HttpResponseMessage response, string error)
    {
        var query = AssertRedirectToClient(response);
        Assert.Equal(error, query["error"]);
        Assert.Equal("xyz", query["state"]);
        Assert.Null(query["code"]);
    }

    private static Dictionary<string, string?> AssertRedirectToClient(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"{(int)response.StatusCode}: {response.Content.ReadAsStringAsync().Result}");
        var location = response.Headers.Location!;
        Assert.StartsWith(RedirectUri, location.GetLeftPart(UriPartial.Path));
        var parsed = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            location.Query.Length > 0 ? location.Query : "?" + location.Fragment.TrimStart('#'));
        return new Dictionary<string, string?>(parsed.Select(p => KeyValuePair.Create(p.Key, (string?)p.Value.ToString())))
        {
            ["code"] = parsed.TryGetValue("code", out var c) ? c.ToString() : null
        };
    }

    private static async Task<WebApplication> StartAsync(bool gateOpen = true)
    {
        var gate = Substitute.For<IAgentAccessGate>();
        gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessDecision(
            gateOpen, gateOpen ? AgentAccessReason.Enabled : AgentAccessReason.TenantSwitchOff));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AgentAccessAuthorizeController).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(gate);
        builder.Services.AddAuthentication("Identity.Application").AddScheme<AuthenticationSchemeOptions, TestCookieHandler>("Identity.Application", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AgentAccessAuthorizeController).Assembly);

        var clients = new List<AgentAccessClientOptions>
        {
            new() { ClientId = "dfrnt-claude", DisplayName = "Claude", Vendor = "Anthropic", RedirectUris = [RedirectUri] }
        };
        var time = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(time);
        var eligibility = Substitute.For<IAgentGrantEligibilityChecker>();
        eligibility.CheckAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AgentGrantEligibility.Eligible);
        builder.Services.AddSingleton(eligibility);
        builder.Services.AddAgentAccessOAuth("Server=unused;Database=unused;", new AgentAccessOAuthSettings(Issuer: Issuer, Clients: clients));

        builder.Services.RemoveAll(typeof(IDbContextOptionsConfiguration<AgentAccessOpenIddictDbContext>));
        var databaseName = Guid.NewGuid().ToString("N");
        builder.Services.AddDbContext<AgentAccessOpenIddictDbContext>(options =>
        {
            options.UseInMemoryDatabase(databaseName);
            options.UseOpenIddict<Guid>();
        });

        var app = builder.Build();
        app.UseRouting();
        app.UseAgentAccessEndpointGate();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    // Stands in for the Hub cookie: the X-Test-User header carries "Claim=value;Claim=value".
    private sealed class TestCookieHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var header)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = header.ToString().Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Select(p => new Claim(p[0], p[1]))
                .Append(new Claim(ClaimTypes.Name, "staff@example.test"))
                .ToList();
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Redirect("/Account/Login?ReturnUrl=" + Uri.EscapeDataString(properties.RedirectUri ?? "/"));
            return Task.CompletedTask;
        }
    }
}
