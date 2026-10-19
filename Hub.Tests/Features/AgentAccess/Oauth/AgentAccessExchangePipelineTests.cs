using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Hub.Features.AgentAccess.Grants;
using Hub.Features.AgentAccess.Oauth;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using OpenIddict.Server;

namespace Hub.Tests.Features.AgentAccess.Oauth;

// Phase 1.9 end to end: the MCP server trades an agent pass for a 15 minute app pass, and every way of getting that
// wrong is refused.
public partial class AgentAccessAuthorizePipelineTests
{
    private const string ExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    private static Task<HttpResponseMessage> TokenExchange(
        WebApplication app,
        string? subjectToken,
        string clientId = "dfrnt-mcp",
        string? secret = "mcp-secret",
        string? subjectType = AccessTokenType)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = ExchangeGrant, ["client_id"] = clientId };
        if (secret is not null) form["client_secret"] = secret;
        if (subjectToken is not null) form["subject_token"] = subjectToken;
        if (subjectType is not null) form["subject_token_type"] = subjectType;
        return Client(app).PostAsync("/connect/token", new FormUrlEncodedContent(form), Ct);
    }

    private static async Task<JwtSecurityToken> AppPass(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return new JwtSecurityTokenHandler().ReadJwtToken(JsonDocument.Parse(text).RootElement.GetProperty("access_token").GetString());
    }

    private static async Task AssertRefused(HttpResponseMessage response, string error, HttpStatusCode? status = null)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains(error, text);
        Assert.Equal(status ?? HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string Forge(
        WebApplication app, string authorizationId = "x", string subject = "55", string tenant = "12",
        string audience = Resource, string issuer = Issuer + "/", TimeSpan? lifetime = null, bool omitAuthorization = false)
    {
        var options = app.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        var claims = new Dictionary<string, object> { ["sub"] = subject, ["tenant_id"] = tenant, ["scope"] = "mcp" };
        if (!omitAuthorization) claims["oi_au_id"] = authorizationId;
        var now = Time(app).GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + (lifetime ?? TimeSpan.FromMinutes(15)),
            SigningCredentials = options.SigningCredentials.First(),
            TokenType = "at+jwt"
        });
    }

    private static string ForgeWithForeignKey(string authorizationId)
    {
        using var rsa = RSA.Create(2048);
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer + "/",
            Audience = Resource,
            Claims = new Dictionary<string, object> { ["sub"] = "55", ["tenant_id"] = "12", ["oi_au_id"] = authorizationId },
            Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa.ExportParameters(true)), SecurityAlgorithms.RsaSha256),
            TokenType = "at+jwt"
        });
    }

    private static string AuthorizationProperty(WebApplication app, string name)
    {
        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
        var json = ctx.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().Single().Properties!;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(name, out var value) ? value.GetString()! : string.Empty;
    }

    [Fact]
    public async Task The_mcp_server_gets_a_fifteen_minute_app_pass_for_the_despatch_apps()
    {
        var (app, tokens, authorizationId) = await ConnectedAsync();
        await using var _ = app;
        await Cache(app).RemoveAsync(GrantKey(authorizationId), Ct);

        var response = await TokenExchange(app, tokens.Access);
        var pass = await AppPass(response);

        Assert.Contains("dfrnt-apps", pass.Audiences);
        Assert.DoesNotContain(Resource, pass.Audiences);
        Assert.Equal(Issuer + "/", pass.Issuer);
        Assert.Equal("55", pass.Subject);
        var issued = long.Parse(pass.Claims.Single(c => c.Type == "iat").Value);
        var expires = long.Parse(pass.Claims.Single(c => c.Type == "exp").Value);
        Assert.Equal(15 * 60, expires - issued);
        Assert.Equal("read", pass.Claims.Single(c => c.Type == "scope").Value);
        Assert.Equal(authorizationId, pass.Claims.Single(c => c.Type == "grant_id").Value);
        Assert.Equal("dfrnt-claude", pass.Claims.Single(c => c.Type == "agent").Value);
        Assert.Equal("agent", pass.Claims.Single(c => c.Type == "auth_kind").Value);
        Assert.Equal("dfrnt-mcp", JsonDocument.Parse(pass.Claims.Single(c => c.Type == "act").Value).RootElement.GetProperty("client_id").GetString());
        Assert.False(string.IsNullOrEmpty(pass.Id));
        Assert.NotNull(await Cache(app).GetAsync(GrantKey(authorizationId), Ct));
    }

    // Group 1 done-when: consent, agent pass, refresh, exchange, revoke.
    [Fact]
    public async Task The_whole_group_one_journey_works_end_to_end_and_revocation_ends_it()
    {
        var (app, first, _) = await ConnectedAsync();
        await using var _ = app;

        var refreshed = (await ReadTokens(await Refresh(app, first.Refresh)))!;
        Time(app).Advance(TimeSpan.FromMinutes(1));
        var pass = await AppPass(await TokenExchange(app, refreshed.Access));
        Assert.Contains("dfrnt-apps", pass.Audiences);

        using (var scope = app.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            await foreach (var authorization in manager.ListAsync(null, null, Ct)) await manager.TryRevokeAsync(authorization, Ct);
        }

        await AssertRefused(await TokenExchange(app, refreshed.Access), "invalid_grant");
        await AssertRefused(await Refresh(app, refreshed.Refresh), "invalid_grant");
    }

    [Fact]
    public async Task The_app_pass_carries_the_cookie_claims_and_never_a_connection_string()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;

        var pass = await AppPass(await TokenExchange(app, tokens.Access));
        var types = pass.Claims.Select(c => c.Type).ToArray();

        foreach (var expected in new[] { "UserID", "CurrentTenantID", "StaffID", "ContactID", "ClientID", "ClientTypeId", "TenantCode", "CountryCode", "TimeZone", "Internal", "IsCourier", "IsNetworkPartner", "NpRoleId", "RoleId", "NpAgentId", "FirstName", "Surname", "AccountsMode" })
        {
            Assert.Contains(expected, types);
        }

        Assert.Equal("7", pass.Claims.Single(c => c.Type == "StaffID").Value);
        Assert.Equal("4", pass.Claims.Single(c => c.Type == "ClientTypeId").Value);
        Assert.Equal("False", pass.Claims.Single(c => c.Type == "IsCourier").Value);
        Assert.DoesNotContain(types, t => t is "Connection" or "RememberMe" or "CourierID");
    }

    [Fact]
    public async Task An_app_pass_comes_with_no_refresh_token()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;

        var body = JsonDocument.Parse(await (await TokenExchange(app, tokens.Access)).Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.False(body.TryGetProperty("refresh_token", out var refresh));
    }

    [Fact]
    public async Task Only_the_mcp_servers_confidential_client_may_exchange()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;

        await AssertRefused(await TokenExchange(app, tokens.Access, secret: "wrong"), "invalid_client", HttpStatusCode.Unauthorized);
        await AssertRefused(await TokenExchange(app, tokens.Access, secret: null), "invalid_client", HttpStatusCode.Unauthorized);
        await AssertRefused(await TokenExchange(app, tokens.Access, clientId: "dfrnt-claude", secret: null), "unauthorized_client");
        await AssertRefused(await TokenExchange(app, tokens.Access, clientId: "nobody", secret: "x"), "invalid_client", HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task With_no_secret_configured_the_exchange_does_not_exist()
    {
        await using var app = await StartAsync(mcpSecret: null);

        await AssertRefused(await TokenExchange(app, "anything"), "invalid_client", HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null, AccessTokenType)]
    [InlineData("", AccessTokenType)]
    [InlineData("a.b.c", null)]
    public async Task A_subject_token_of_the_right_type_is_required(string? subject, string? type)
    {
        await using var app = await StartAsync();

        await AssertRefused(await TokenExchange(app, subject, subjectType: type), "invalid_request");
    }

    [Theory]
    [InlineData("urn:ietf:params:oauth:token-type:refresh_token")]
    [InlineData("urn:ietf:params:oauth:token-type:jwt")]
    [InlineData("urn:ietf:params:oauth:token-type:id_token")]
    public async Task Only_an_access_token_may_be_exchanged(string type)
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;

        var response = await TokenExchange(app, tokens.Access, subjectType: type);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Garbage_and_tampered_tokens_are_refused()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        var parts = tokens.Access.Split('.');
        var tampered = parts[0] + "." + parts[1] + "." + new string(parts[2].Reverse().ToArray());

        await AssertRefused(await TokenExchange(app, "not-a-token"), "invalid_grant");
        await AssertRefused(await TokenExchange(app, tampered), "invalid_grant");
        await AssertRefused(await TokenExchange(app, new string('a', 9000)), "invalid_grant");
    }

    [Fact]
    public async Task A_token_signed_by_someone_else_is_refused_even_with_the_right_claims()
    {
        var (app, _, authorizationId) = await ConnectedAsync();
        await using var _ = app;

        await AssertRefused(await TokenExchange(app, ForgeWithForeignKey(authorizationId)), "invalid_grant");
    }

    [Theory]
    [InlineData("https://hub.other-tenant.example.test/")]
    [InlineData("https://hub.urgent.example.test")]
    public async Task A_token_from_another_issuer_is_refused(string issuer)
    {
        var (app, _, authorizationId) = await ConnectedAsync();
        await using var _ = app;

        await AssertRefused(await TokenExchange(app, Forge(app, authorizationId, issuer: issuer)), "invalid_grant");
    }

    [Theory]
    [InlineData("dfrnt-apps")]
    [InlineData("https://mcp.other.example.test/mcp")]
    public async Task A_token_for_another_audience_is_refused_including_an_app_pass(string audience)
    {
        var (app, _, authorizationId) = await ConnectedAsync();
        await using var _ = app;

        await AssertRefused(await TokenExchange(app, Forge(app, authorizationId, audience: audience)), "invalid_grant");
    }

    [Fact]
    public async Task An_expired_agent_pass_is_refused()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        Time(app).Advance(TimeSpan.FromMinutes(21));

        await AssertRefused(await TokenExchange(app, tokens.Access), "invalid_grant");
    }

    [Fact]
    public async Task A_valid_token_that_does_not_match_its_authorization_is_refused()
    {
        var (app, _, authorizationId) = await ConnectedAsync();
        await using var _ = app;

        await AssertRefused(await TokenExchange(app, Forge(app, authorizationId, tenant: "99")), "invalid_grant");
        await AssertRefused(await TokenExchange(app, Forge(app, authorizationId, subject: "56")), "invalid_grant");
        await AssertRefused(await TokenExchange(app, Forge(app, Guid.NewGuid().ToString())), "invalid_grant");
        await AssertRefused(await TokenExchange(app, Forge(app, omitAuthorization: true)), "invalid_grant");
    }

    [Fact]
    public async Task A_revoked_grant_cannot_be_exchanged()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        using (var scope = app.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            await foreach (var authorization in manager.ListAsync(null, null, Ct)) await manager.TryRevokeAsync(authorization, Ct);
        }

        await AssertRefused(await TokenExchange(app, tokens.Access), "invalid_grant");
    }

    [Fact]
    public async Task A_deactivated_user_cannot_exchange_and_the_grant_is_revoked()
    {
        var (app, tokens, authorizationId) = await ConnectedAsync();
        await using var _ = app;
        app.Services.GetRequiredService<IAgentPassIdentityLoader>()
            .LoadAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPassLoad(AgentGrantEligibility.NoLongerEligible, null));

        await AssertRefused(await TokenExchange(app, tokens.Access), "invalid_grant");

        Assert.Equal("revoked", AuthorizationStatus(app));
        Assert.Null(await Cache(app).GetAsync(GrantKey(authorizationId), Ct));
    }

    [Fact]
    public async Task When_the_user_cannot_be_checked_the_exchange_fails_but_the_grant_survives()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        app.Services.GetRequiredService<IAgentPassIdentityLoader>()
            .LoadAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPassLoad(AgentGrantEligibility.Unknown, null));

        await AssertRefused(await TokenExchange(app, tokens.Access), "invalid_grant");

        Assert.Equal("valid", AuthorizationStatus(app));
    }

    [Fact]
    public async Task The_tenant_off_flag_stops_the_exchange_and_clearing_it_resumes()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        await Cache(app).SetAsync("agent-access:tenant:12:off", [1], Ct);

        await AssertRefused(await TokenExchange(app, tokens.Access), "invalid_grant");

        await Cache(app).RemoveAsync("agent-access:tenant:12:off", Ct);
        Assert.Equal(HttpStatusCode.OK, (await TokenExchange(app, tokens.Access)).StatusCode);
    }

    [Fact]
    public async Task An_exchange_records_when_the_grant_was_last_used_but_not_on_every_call()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        Assert.Equal(string.Empty, AuthorizationProperty(app, "last_used_utc"));

        await TokenExchange(app, tokens.Access);
        var first = AuthorizationProperty(app, "last_used_utc");
        Time(app).Advance(TimeSpan.FromMinutes(2));
        await TokenExchange(app, tokens.Access);
        var second = AuthorizationProperty(app, "last_used_utc");
        Time(app).Advance(TimeSpan.FromMinutes(6));
        await TokenExchange(app, await ReFreshAccess(app, tokens));
        var third = AuthorizationProperty(app, "last_used_utc");

        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
    }

    private static async Task<string> ReFreshAccess(WebApplication app, Tokens tokens) =>
        (await ReadTokens(await Refresh(app, tokens.Refresh)))!.Access;
}
