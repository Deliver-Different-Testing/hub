using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hub.Features.AgentAccess;
using Hub.Features.AgentAccess.ClientMetadata;
using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Grants;
using Hub.Features.AgentAccess.Keys;
using Hub.Features.AgentAccess.Oauth;
using Hub.Interfaces;
using Hub.Models.Master;
using Hub.Tests.Features.AgentAccess.Keys;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenIddict.EntityFrameworkCore.Models;

namespace Hub.Tests.Features.AgentAccess.Oauth;

// Phase 1.5 end to end on a real ASP.NET Core pipeline: OpenIddict registered exactly as Hub registers it,
// the open gate, a stand-in for the Hub cookie, the real controllers and the connect-page API the SPA reads.
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
        string? resource = Resource, string scope = "mcp offline_access", string? prompt = null, string clientId = "dfrnt-claude",
        string redirectUri = RedirectUri)
    {
        var query = new List<string>
        {
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
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

        var (page, response) = await Page(app, AuthorizeUrl(), Staff(clientType));

        Assert.Equal("consent", page.GetProperty("kind").GetString());
        Assert.Equal("Claude", page.GetProperty("clientName").GetString());
        Assert.Equal("Anthropic", page.GetProperty("verifiedVendor").GetString());
        Assert.False(page.TryGetProperty("verified", out _));
        Assert.Equal("mcp.urgent.example.test", page.GetProperty("resourceHost").GetString());
        Assert.Equal("claude.ai", page.GetProperty("redirectHost").GetString());
        Assert.Equal("__RequestVerificationToken", page.GetProperty("antiforgeryFieldName").GetString());
        Assert.False(string.IsNullOrEmpty(page.GetProperty("antiforgeryToken").GetString()));
        Assert.Equal("xyz", Fields(page)["state"]);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task The_connect_page_is_an_spa_route_that_cannot_be_framed_or_cached()
    {
        await using var app = await StartAsync();

        var response = await Get(app, "/agent-access/connect?ticket=x", Staff());

        Assert.Equal("shell", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Other_spa_routes_keep_their_own_headers()
    {
        await using var app = await StartAsync();

        var response = await Get(app, "/FuelSurcharge", Staff());

        Assert.False(response.Headers.Contains("X-Frame-Options"));
    }

    [Fact]
    public async Task A_tampered_ticket_is_refused()
    {
        await using var app = await StartAsync();

        var response = await Get(app, "/api/agent-access/connect?ticket=not-a-ticket", Staff());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Couldn't open this connection request. It has expired.", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_ticket_only_opens_for_the_person_it_was_issued_to()
    {
        await using var app = await StartAsync();
        var ticket = Uri.EscapeDataString(await Ticket(app, AuthorizeUrl(), Staff()));

        var otherUser = await Get(app, "/api/agent-access/connect?ticket=" + ticket, Staff().Replace("UserID=55", "UserID=56"));
        var otherTenant = await Get(app, "/api/agent-access/connect?ticket=" + ticket, Staff(tenantId: "3"));

        Assert.Equal(HttpStatusCode.Forbidden, otherUser.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherTenant.StatusCode);
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

    // ---- Claude Code: client ID metadata document with loopback redirects (RFC 8252) ----

    private const string ClaudeCodeClientId = "https://claude.ai/oauth/claude-code-client-metadata";

    private static async Task<WebApplication> StartWithClaudeCodeAsync()
    {
        var app = await StartAsync(metadataHosts: ["claude.ai/oauth/"]);
        app.Services.GetRequiredService<IClientMetadataFetcher>()
            .FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(new ClientMetadataFetchResult(
                $$"""{"client_id":"{{ClaudeCodeClientId}}","client_name":"Claude Code","client_uri":"https://claude.ai","redirect_uris":["http://localhost/callback","http://127.0.0.1/callback"],"grant_types":["authorization_code","refresh_token"],"response_types":["code"],"token_endpoint_auth_method":"none"}""",
                null));
        return app;
    }

    [Fact]
    public async Task Claude_code_signs_in_on_any_loopback_port_and_redeems_its_code()
    {
        await using var app = await StartWithClaudeCodeAsync();
        const string loopback = "http://localhost:44945/callback";

        var response = await Consent(app, AuthorizeUrl(clientId: ClaudeCodeClientId, redirectUri: loopback, prompt: "consent"), "allow");
        var code = AssertRedirectToClient(response, loopback)["code"]!;
        var token = await Token(app, code, Verifier, ClaudeCodeClientId, loopback);

        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:44945/other")]
    [InlineData("http://evil.test:44945/callback")]
    [InlineData("https://localhost:44945/callback")]
    public async Task Claude_code_redirects_off_its_registered_loopback_paths_are_refused(string redirectUri)
    {
        await using var app = await StartWithClaudeCodeAsync();

        var response = await Get(app, AuthorizeUrl(clientId: ClaudeCodeClientId, redirectUri: redirectUri), Staff());

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task With_several_created_signing_keys_the_earliest_written_signs_and_every_one_is_published()
    {
        var (earliest, later, latest) = (AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());
        var parameters = new Dictionary<string, string>
        {
            [AgentAccessKeyLoader.EncryptionKeyName] = AgentAccessKeyLoaderTests.Pem(),
            ["signing/20261105000000-ffffffff"] = earliest,
            ["signing/20261106000000-00000000"] = later,
            ["signing/20261104000000-00000000"] = latest
        };
        // Neither the highest nor the lowest kid: only the write dates make "earliest" sign.
        var dates = new Dictionary<string, DateTime>
        {
            ["signing/20261105000000-ffffffff"] = AgentAccessKeyLoaderTests.Written,
            ["signing/20261106000000-00000000"] = AgentAccessKeyLoaderTests.Written.AddSeconds(1),
            ["signing/20261104000000-00000000"] = AgentAccessKeyLoaderTests.Written.AddSeconds(2)
        };
        var keySource = Substitute.For<IAgentAccessKeySource>();
        keySource.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyDictionary<string, AgentAccessKeyParameter>)AgentAccessKeyLoaderTests.Stored(parameters, dates));
        await using var app = await StartAsync(keySource: keySource);
        var code = AssertRedirectToClient(await Consent(app, AuthorizeUrl(), "allow"))["code"]!;

        var response = await Token(app, code, Verifier);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        var jwks = JsonDocument.Parse(await Client(app).GetStringAsync("/.well-known/jwks", Ct)).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.GetProperty("access_token").GetString());
        Assert.Equal(AgentAccessKeyLoaderTests.KidOf(earliest), jwt.Header.Kid);
        Assert.Equal(
            new[] { earliest, later, latest }.Select(AgentAccessKeyLoaderTests.KidOf).Order(),
            jwks.GetProperty("keys").EnumerateArray().Select(k => k.GetProperty("kid").GetString()).Order());
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

    [Theory]
    [InlineData(AgentGrantEligibility.NotPermitted, "access_denied")]
    [InlineData(AgentGrantEligibility.NoLongerEligible, "access_denied")]
    [InlineData(AgentGrantEligibility.TenantOff, "access_denied")]
    [InlineData(AgentGrantEligibility.Unknown, "temporarily_unavailable")]
    public async Task Someone_who_could_not_get_a_pass_is_refused_before_the_consent_page(AgentGrantEligibility eligibility, string error)
    {
        // Access Control: a role without agent-connect cannot connect at all, rather than consent and then fail every call.
        await using var app = await StartAsync();
        RefusePasses(app, eligibility);

        var response = await Get(app, AuthorizeUrl(), Staff());

        AssertErrorRedirect(response, error);
    }

    [Fact]
    public async Task An_existing_authorization_does_not_skip_the_role_check()
    {
        await using var app = await StartAsync();
        await Consent(app, AuthorizeUrl(), "allow");
        RefusePasses(app, AgentGrantEligibility.NotPermitted);

        var response = await Get(app, AuthorizeUrl(), Staff());

        AssertErrorRedirect(response, "access_denied");
    }

    private static void RefusePasses(WebApplication app, AgentGrantEligibility eligibility) =>
        app.Services.GetRequiredService<IAgentPassIdentityLoader>()
            .LoadAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPassLoad(eligibility, null));

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

        AssertSentToConnectPage(response);
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

        AssertSentToConnectPage(response);
    }

    [Fact]
    public async Task Posting_without_the_antiforgery_token_is_rejected_and_creates_nothing()
    {
        await using var app = await StartAsync();
        var (page, _) = await Page(app, AuthorizeUrl(), Staff());
        var fields = Fields(page);
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

    // The authorize endpoint sends the browser to the SPA's connect page with a sealed ticket; the page reads it back.
    private static async Task<string> Ticket(WebApplication app, string url, string user)
    {
        var response = await Get(app, url, user);
        AssertSentToConnectPage(response);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), response.Headers.Location!).Query);
        return query["ticket"].ToString();
    }

    private static async Task<(JsonElement Page, HttpResponseMessage Response)> Page(WebApplication app, string url, string user)
    {
        var ticket = await Ticket(app, url, user);
        var response = await Get(app, "/api/agent-access/connect?ticket=" + Uri.EscapeDataString(ticket), user);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement, response);
    }

    private static void AssertSentToConnectPage(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/agent-access/connect?ticket=", response.Headers.Location!.OriginalString);
    }

    // What the SPA's form posts back: the original authorization request, one hidden field per parameter.
    private static Dictionary<string, string> Fields(JsonElement page) =>
        page.GetProperty("parameters").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("value").GetString()!);

    private static async Task<HttpResponseMessage> Consent(WebApplication app, string url, string decision, string? user = null)
    {
        user ??= Staff();
        var (page, response) = await Page(app, url, user);
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var set)
            ? string.Join("; ", set.Select(c => c.Split(';')[0]))
            : string.Empty;

        var fields = Fields(page);
        fields["decision"] = decision;
        fields[page.GetProperty("antiforgeryFieldName").GetString()!] = page.GetProperty("antiforgeryToken").GetString()!;

        var post = new HttpRequestMessage(HttpMethod.Post, "/connect/authorize")
        {
            Content = new FormUrlEncodedContent(fields)
        };
        post.Headers.Add("X-Test-User", user);
        if (cookies.Length > 0) post.Headers.Add("Cookie", cookies);
        return await Client(app).SendAsync(post, Ct);
    }

    private static Task<HttpResponseMessage> Token(
        WebApplication app, string code, string verifier, string clientId = "dfrnt-claude", string redirectUri = RedirectUri) =>
        Client(app).PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier
        }), Ct);

    private static void AssertErrorRedirect(HttpResponseMessage response, string error)
    {
        var query = AssertRedirectToClient(response);
        Assert.Equal(error, query["error"]);
        Assert.Equal("xyz", query["state"]);
        Assert.Null(query["code"]);
    }

    private static Dictionary<string, string?> AssertRedirectToClient(HttpResponseMessage response, string redirectUri = RedirectUri)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"{(int)response.StatusCode}: {response.Content.ReadAsStringAsync().Result}");
        var location = response.Headers.Location!;
        Assert.StartsWith(redirectUri, location.GetLeftPart(UriPartial.Path));
        var parsed = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            location.Query.Length > 0 ? location.Query : "?" + location.Fragment.TrimStart('#'));
        return new Dictionary<string, string?>(parsed.Select(p => KeyValuePair.Create(p.Key, (string?)p.Value.ToString())))
        {
            ["code"] = parsed.TryGetValue("code", out var c) ? c.ToString() : null
        };
    }

    private const int HubTenantId = 12;
    private const string HubTenantName = "Urgent Couriers";

    private static async Task<WebApplication> StartAsync(
        bool gateOpen = true, string[]? metadataHosts = null, string? mcpSecret = "mcp-secret",
        AgentAccessReason closedReason = AgentAccessReason.Error, IAgentAccessKeySource? keySource = null,
        string? clusterIssuer = ClusterIssuer, string[]? mcpServiceAccounts = null, string? issuer = Issuer,
        string? antiforgeryFieldName = null)
    {
        var gate = Substitute.For<IAgentAccessGate>();
        gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessDecision(
            gateOpen, gateOpen ? AgentAccessReason.Enabled : closedReason));
        var tenants = Substitute.For<IAgentAccessTenantResolver>();
        tenants.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessTenant(HubTenantId, "urgent", HubTenantName));
        var master = Substitute.For<IAuthenticationRepository>();
        master.GetTenantByIdAsync(Arg.Any<int>()).Returns(ci => new Tenant { TenantId = ci.Arg<int>(), Code = "dfrnt", Name = "DFRNT" });
        master.IsUserAssociatedWithTenantAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(true);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AgentAccessAuthorizeController).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(gate);
        builder.Services.AddSingleton(tenants);
        builder.Services.AddSingleton(master);
        builder.Services.AddSingleton(Substitute.For<IAccountService>());
        builder.Services.AddAuthentication("Identity.Application").AddScheme<AuthenticationSchemeOptions, TestCookieHandler>("Identity.Application", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AgentAccessAuthorizeController).Assembly);
        if (antiforgeryFieldName is not null) builder.Services.Configure<AntiforgeryOptions>(o => o.FormFieldName = antiforgeryFieldName);

        var clients = new List<AgentAccessClientOptions>
        {
            new() { ClientId = "dfrnt-claude", DisplayName = "Claude", Vendor = "Anthropic", RedirectUris = [RedirectUri] }
        };
        var time = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(time);
        var identities = Substitute.For<IAgentPassIdentityLoader>();
        identities.LoadAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPassLoad(AgentGrantEligibility.Eligible, new AgentPassIdentity("staff@urgent.test", 55, 12, 9, 3, 7, "NZ", "New Zealand Standard Time", "urgent", true, false, 2, 4, null, "Sam", "Staff", 1)));
        builder.Services.AddSingleton(identities);
        builder.Services.AddAgentAccessOAuth("Server=unused;Database=unused;", new AgentAccessOAuthSettings(
            SsmPrefix: keySource is null ? null : "/hub/urgent/agent-access", Issuer: issuer, KeySource: keySource,
            Clients: clients, ClientMetadataHosts: metadataHosts ?? ["claude.ai"], McpClientSecret: mcpSecret,
            ClusterIssuer: clusterIssuer, McpServiceAccounts: mcpServiceAccounts ?? [McpServiceAccount]));
        var cluster = new ClusterOidcStub();
        builder.Services.AddSingleton(cluster);
        builder.Services.AddHttpClient(AgentAccessClusterJwks.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => cluster);
        var metadataFetcher = Substitute.For<IClientMetadataFetcher>();
        builder.Services.RemoveAll(typeof(IClientMetadataFetcher));
        builder.Services.AddSingleton(metadataFetcher);

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
        app.UseAgentAccessPageHeaders();
        app.UseAuthorization();
        app.MapControllers();
        // Stands in for Program's MapFallbackToFile("index.html"): every other path is the SPA shell.
        app.MapFallback(context => context.Response.WriteAsync("shell"));
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
