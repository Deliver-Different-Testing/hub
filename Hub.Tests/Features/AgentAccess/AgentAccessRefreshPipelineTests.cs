using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Hub.Features.AgentAccess;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Tests.Features.AgentAccess;

// Phase 1.6 end to end: the Redis liveness key, the refresh grant, rotation and reuse, the twelve month cap, a
// deactivated user, and the tenant-off flag, all through the real token endpoint.
public partial class AgentAccessAuthorizePipelineTests
{
    private sealed class AdjustableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed record Tokens(string Access, string Refresh);

    private static async Task<(WebApplication App, Tokens Tokens, string AuthorizationId)> ConnectedAsync()
    {
        var app = await StartAsync();
        var code = AssertRedirectToClient(await Consent(app, AuthorizeUrl(), "allow"))["code"]!;
        var tokens = await ReadTokens(await Token(app, code, Verifier));
        return (app, tokens!, AuthorizationId(app));
    }

    private static string AuthorizationId(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
        return ctx.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().Single().Id.ToString();
    }

    private static string AuthorizationStatus(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
        return ctx.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().Single().Status!;
    }

    private static async Task<Tokens?> ReadTokens(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        return new Tokens(body.GetProperty("access_token").GetString()!, body.GetProperty("refresh_token").GetString()!);
    }

    private static Task<HttpResponseMessage> Refresh(WebApplication app, string refreshToken, string? scope = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "dfrnt-claude",
            ["refresh_token"] = refreshToken
        };
        if (scope is not null) form["scope"] = scope;
        return Client(app).PostAsync("/connect/token", new FormUrlEncodedContent(form), Ct);
    }

    private static async Task AssertInvalidGrant(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_grant", body);
    }

    private static IDistributedCache Cache(WebApplication app) => app.Services.GetRequiredService<IDistributedCache>();

    private static string GrantKey(string authorizationId) => "agent-grant:" + authorizationId;

    private static AdjustableTimeProvider Time(WebApplication app) => app.Services.GetRequiredService<AdjustableTimeProvider>();

    private static void SetEligibility(WebApplication app, AgentGrantEligibility value) =>
        app.Services.GetRequiredService<IAgentPassIdentityLoader>()
            .LoadAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPassLoad(value, value == AgentGrantEligibility.Eligible ? DefaultIdentity : null));

    private static readonly AgentPassIdentity DefaultIdentity =
        new("staff@urgent.test", 55, 12, 9, 3, 7, "NZ", "New Zealand Standard Time", "urgent", true, false, 2, 4, null, "Sam", "Staff", 1);

    [Fact]
    public async Task Consent_and_the_code_exchange_set_the_grant_key()
    {
        var (app, _, authorizationId) = await ConnectedAsync();
        await using var _ = app;

        Assert.NotNull(await Cache(app).GetAsync(GrantKey(authorizationId), Ct));
    }

    [Fact]
    public async Task A_refresh_returns_new_tokens_for_the_same_grant_and_resets_the_key()
    {
        var (app, tokens, authorizationId) = await ConnectedAsync();
        await using var _ = app;
        await Cache(app).RemoveAsync(GrantKey(authorizationId), Ct);

        var refreshed = await ReadTokens(await Refresh(app, tokens.Refresh));

        Assert.NotEqual(tokens.Refresh, refreshed!.Refresh);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(refreshed.Access);
        Assert.Contains(Resource, jwt.Audiences);
        Assert.Equal("55", jwt.Subject);
        Assert.Equal("12", jwt.Claims.Single(c => c.Type == "tenant_id").Value);
        Assert.NotNull(await Cache(app).GetAsync(GrantKey(authorizationId), Ct));
    }

    [Fact]
    public async Task A_flushed_redis_recovers_on_the_next_refresh_and_never_blocks_it()
    {
        var (app, tokens, authorizationId) = await ConnectedAsync();
        await using var _ = app;
        await Cache(app).RemoveAsync(GrantKey(authorizationId), Ct);
        Assert.Null(await Cache(app).GetAsync(GrantKey(authorizationId), Ct));

        Assert.Equal(HttpStatusCode.OK, (await Refresh(app, tokens.Refresh)).StatusCode);

        Assert.NotNull(await Cache(app).GetAsync(GrantKey(authorizationId), Ct));
    }

    [Fact]
    public async Task Reusing_a_refresh_token_inside_the_thirty_second_leeway_still_works()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        await ReadTokens(await Refresh(app, tokens.Refresh));
        Time(app).Advance(TimeSpan.FromSeconds(20));

        var again = await Refresh(app, tokens.Refresh);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_refresh_token_after_the_leeway_is_refused()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        await ReadTokens(await Refresh(app, tokens.Refresh));
        Time(app).Advance(TimeSpan.FromSeconds(31));

        await AssertInvalidGrant(await Refresh(app, tokens.Refresh));
    }

    // OpenIddict ends the whole family on reuse by revoking every token of the authorization in one statement. That
    // statement is a set update the in-memory test store cannot run (it is verified against SQL Server), so this
    // pins the part we own: every token we issue, original and rotated, belongs to the one authorization.
    [Fact]
    public async Task Every_token_issued_across_rotations_belongs_to_the_one_authorization()
    {
        var (app, tokens, authorizationId) = await ConnectedAsync();
        await using var _ = app;
        var second = (await ReadTokens(await Refresh(app, tokens.Refresh)))!;
        await ReadTokens(await Refresh(app, second.Refresh));

        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
        var owners = ctx.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().Select(x => EF.Property<Guid?>(x, "AuthorizationId")).ToList();
        Assert.True(owners.Count >= 4);
        Assert.All(owners, owner => Assert.Equal(authorizationId, owner.ToString()));
    }

    [Fact]
    public async Task Refreshing_keeps_working_across_months_but_stops_at_twelve()
    {
        var (app, tokens, authorizationId) = await ConnectedAsync();
        await using var _ = app;
        var current = tokens;

        for (var day = 60; day <= 300; day += 60)
        {
            Time(app).Advance(TimeSpan.FromDays(60));
            current = (await ReadTokens(await Refresh(app, current.Refresh)))!;
        }

        Time(app).Advance(TimeSpan.FromDays(66));
        await AssertInvalidGrant(await Refresh(app, current.Refresh));
        Assert.Equal("revoked", AuthorizationStatus(app));
        Assert.Null(await Cache(app).GetAsync(GrantKey(authorizationId), Ct));
    }

    [Fact]
    public async Task A_deactivated_user_cannot_refresh_and_the_grant_is_revoked()
    {
        var (app, tokens, authorizationId) = await ConnectedAsync();
        await using var _ = app;
        SetEligibility(app, AgentGrantEligibility.NoLongerEligible);

        await AssertInvalidGrant(await Refresh(app, tokens.Refresh));

        Assert.Equal("revoked", AuthorizationStatus(app));
        Assert.Null(await Cache(app).GetAsync(GrantKey(authorizationId), Ct));
        SetEligibility(app, AgentGrantEligibility.Eligible);
        await AssertInvalidGrant(await Refresh(app, tokens.Refresh));
    }

    [Fact]
    public async Task When_the_user_cannot_be_checked_the_refresh_fails_but_the_grant_survives()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        SetEligibility(app, AgentGrantEligibility.Unknown);

        await AssertInvalidGrant(await Refresh(app, tokens.Refresh));

        Assert.Equal("valid", AuthorizationStatus(app));
        SetEligibility(app, AgentGrantEligibility.Eligible);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(app, tokens.Refresh)).StatusCode);
    }

    [Fact]
    public async Task The_tenant_off_flag_stops_a_refresh_without_revoking_and_it_resumes_when_cleared()
    {
        var (app, tokens, authorizationId) = await ConnectedAsync();
        await using var _ = app;
        await Cache(app).SetAsync("agent-access:tenant:12:off", [1], Ct);
        await Cache(app).RemoveAsync(GrantKey(authorizationId), Ct);

        await AssertInvalidGrant(await Refresh(app, tokens.Refresh));

        Assert.Equal("valid", AuthorizationStatus(app));
        Assert.Null(await Cache(app).GetAsync(GrantKey(authorizationId), Ct));
        await Cache(app).RemoveAsync("agent-access:tenant:12:off", Ct);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(app, tokens.Refresh)).StatusCode);
    }

    [Fact]
    public async Task A_revoked_authorization_cannot_refresh()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        using (var scope = app.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            await foreach (var authorization in manager.ListAsync(null, null, Ct))
            {
                await manager.TryRevokeAsync(authorization, Ct);
            }
        }

        await AssertInvalidGrant(await Refresh(app, tokens.Refresh));
    }

    [Fact]
    public async Task A_refresh_cannot_widen_what_was_approved()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;

        var response = await Refresh(app, tokens.Refresh, scope: "mcp offline_access admin");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var access = (await ReadTokens(response))!.Access;
            var scopes = new JwtSecurityTokenHandler().ReadJwtToken(access).Claims
                .Where(c => c.Type == "scope").SelectMany(c => c.Value.Split(' '));
            Assert.DoesNotContain("admin", scopes);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_refused()
    {
        await using var app = await StartAsync();

        await AssertInvalidGrant(await Refresh(app, "not-a-token"));
    }
}
