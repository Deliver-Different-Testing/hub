using System.Net;
using System.Text.Json;
using Hub.Features.AgentAccess.Gate;
using Hub.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Tests.Features.AgentAccess.Oauth;

// A Hub serves one tenant for agent access. Someone signed in to another tenant (only DFRNT staff belong to more than
// one) is told and offered a switch; a tenant that has switched agent access off is told so instead of hidden.
public partial class AgentAccessAuthorizePipelineTests
{
    // ---- switched off --------------------------------------------------------

    [Fact]
    public async Task A_switched_off_tenant_sends_authorize_to_the_unavailable_page()
    {
        await using var app = await StartAsync(gateOpen: false, closedReason: AgentAccessReason.TenantSwitchOff);

        var response = await Get(app, AuthorizeUrl(), Staff());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/agent-access/unavailable", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData(AgentAccessReason.TenantSwitchOff, "AI app access is switched off for Urgent Couriers")]
    [InlineData(AgentAccessReason.ForcedOff, "switched off for Urgent Couriers by DFRNT")]
    public async Task The_unavailable_page_says_why(AgentAccessReason reason, string expected)
    {
        await using var app = await StartAsync(gateOpen: false, closedReason: reason);

        var response = await Get(app, "/api/agent-access/unavailable", user: null);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, JsonDocument.Parse(body).RootElement.GetProperty("message").GetString());
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task The_unavailable_page_is_an_spa_route_that_cannot_be_framed()
    {
        await using var app = await StartAsync(gateOpen: false, closedReason: AgentAccessReason.TenantSwitchOff);

        var response = await Get(app, "/agent-access/unavailable", user: null);

        Assert.Equal("shell", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Theory]
    [InlineData(AgentAccessReason.KeysNotPersistent)]
    [InlineData(AgentAccessReason.Error)]
    public async Task The_unavailable_page_does_not_exist_while_agent_access_is_dark(AgentAccessReason reason)
    {
        await using var app = await StartAsync(gateOpen: false, closedReason: reason);

        Assert.Equal(HttpStatusCode.NotFound, (await Get(app, "/api/agent-access/unavailable", user: null)).StatusCode);
    }

    [Fact]
    public async Task The_unavailable_page_says_to_try_again_once_access_is_back_on()
    {
        await using var app = await StartAsync();

        var body = await (await Get(app, "/api/agent-access/unavailable", user: null)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("AI app access is on for Urgent Couriers", body);
    }

    // ---- signed in to another tenant -----------------------------------------

    [Fact]
    public async Task Signed_in_to_another_tenant_is_told_and_offered_a_switch()
    {
        await using var app = await StartAsync();

        var (page, _) = await Page(app, AuthorizeUrl(), Staff(tenantId: "3"));

        Assert.Equal("switch", page.GetProperty("kind").GetString());
        Assert.Equal("DFRNT", page.GetProperty("currentOrganisation").GetString());
        Assert.Equal("Urgent Couriers", page.GetProperty("hubOrganisation").GetString());
        Assert.True(page.GetProperty("canSwitch").GetBoolean());
        Assert.Equal("xyz", Fields(page)["state"]);
    }

    [Fact]
    public async Task Switching_moves_the_user_and_starts_the_authorization_again()
    {
        await using var app = await StartAsync();
        var accounts = app.Services.GetRequiredService<IAccountService>();
        accounts.UpdateCurrentTenantAsync(Arg.Any<HttpContext>(), HubTenantId)
            .Returns(new UpdateTenantResult(true, null, "https://hub.urgent.staging.deliverdifferent.com/Account/AcceptTenantSwitchToken?t=x"));

        var response = await Consent(app, AuthorizeUrl(), "switch", Staff(tenantId: "3"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/connect/authorize?", location);
        Assert.Contains("client_id=dfrnt-claude", location);
        Assert.Contains("state=xyz", location);
        Assert.DoesNotContain("decision", location);
        Assert.DoesNotContain("__RequestVerificationToken", location);
        await accounts.Received(1).UpdateCurrentTenantAsync(Arg.Any<HttpContext>(), HubTenantId);
    }

    [Fact]
    public async Task A_failed_switch_shows_why_and_issues_nothing()
    {
        await using var app = await StartAsync();
        app.Services.GetRequiredService<IAccountService>()
            .UpdateCurrentTenantAsync(Arg.Any<HttpContext>(), HubTenantId)
            .Returns(UpdateTenantResult.Fail("Your account isn't set up in that organisation yet. Ask your administrator to add you."));

        var response = await Consent(app, AuthorizeUrl(), "switch", Staff(tenantId: "3"));
        var page = await PageFrom(app, response, Staff(tenantId: "3"));

        Assert.Equal("switch", page.GetProperty("kind").GetString());
        Assert.Contains("set up in that organisation yet", page.GetProperty("error").GetString());
        Assert.Equal("xyz", Fields(page)["state"]);
    }

    [Fact]
    public async Task The_antiforgery_token_never_rides_into_the_next_ticket_whatever_its_field_is_called()
    {
        await using var app = await StartAsync(antiforgeryFieldName: "af-token");
        app.Services.GetRequiredService<IAccountService>()
            .UpdateCurrentTenantAsync(Arg.Any<HttpContext>(), HubTenantId)
            .Returns(UpdateTenantResult.Fail("Couldn't switch organisation. Try again."));

        var response = await Consent(app, AuthorizeUrl(), "switch", Staff(tenantId: "3"));
        var page = await PageFrom(app, response, Staff(tenantId: "3"));

        Assert.Equal("af-token", page.GetProperty("antiforgeryFieldName").GetString());
        Assert.DoesNotContain("af-token", Fields(page).Keys);
        Assert.Equal("xyz", Fields(page)["state"]);
    }

    [Fact]
    public async Task Cancelling_the_switch_tells_the_app_access_was_denied()
    {
        await using var app = await StartAsync();

        var response = await Consent(app, AuthorizeUrl(), "cancel", Staff(tenantId: "3"));

        AssertErrorRedirect(response, Errors.AccessDenied);
        await app.Services.GetRequiredService<IAccountService>()
            .DidNotReceive().UpdateCurrentTenantAsync(Arg.Any<HttpContext>(), Arg.Any<int>());
    }

    [Fact]
    public async Task Someone_outside_the_tenant_is_not_offered_a_switch()
    {
        await using var app = await StartAsync();
        app.Services.GetRequiredService<IAuthenticationRepository>()
            .IsUserAssociatedWithTenantAsync(55, HubTenantId).Returns(false);

        var (page, _) = await Page(app, AuthorizeUrl(), Staff(tenantId: "3"));

        Assert.Equal("switch", page.GetProperty("kind").GetString());
        Assert.False(page.GetProperty("canSwitch").GetBoolean());
    }

    [Fact]
    public async Task Someone_outside_the_tenant_cannot_post_a_switch_anyway()
    {
        await using var app = await StartAsync();
        var accounts = app.Services.GetRequiredService<IAccountService>();
        app.Services.GetRequiredService<IAuthenticationRepository>()
            .IsUserAssociatedWithTenantAsync(55, HubTenantId).Returns(false);

        await Consent(app, AuthorizeUrl(), "switch", Staff(tenantId: "3"));

        await accounts.DidNotReceive().UpdateCurrentTenantAsync(Arg.Any<HttpContext>(), Arg.Any<int>());
    }

    [Fact]
    public async Task A_switch_without_the_antiforgery_token_is_rejected()
    {
        await using var app = await StartAsync();
        var (page, _) = await Page(app, AuthorizeUrl(), Staff(tenantId: "3"));
        var fields = Fields(page);
        fields["decision"] = "switch";
        var request = new HttpRequestMessage(HttpMethod.Post, AuthorizeUrl()) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("X-Test-User", Staff(tenantId: "3"));

        var response = await Client(app).SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await app.Services.GetRequiredService<IAccountService>()
            .DidNotReceive().UpdateCurrentTenantAsync(Arg.Any<HttpContext>(), Arg.Any<int>());
    }

    [Fact]
    public async Task Signed_in_to_the_hubs_tenant_goes_straight_to_consent()
    {
        await using var app = await StartAsync();

        var (page, _) = await Page(app, AuthorizeUrl(), Staff(tenantId: "12"));

        Assert.Equal("consent", page.GetProperty("kind").GetString());
    }

    // A failed switch sends the browser back to the connect page with a fresh ticket carrying the error.
    private static async Task<JsonElement> PageFrom(WebApplication app, HttpResponseMessage response, string user)
    {
        AssertSentToConnectPage(response);
        var ticket = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), response.Headers.Location!).Query)["ticket"].ToString();
        var page = await Get(app, "/api/agent-access/connect?ticket=" + Uri.EscapeDataString(ticket), user);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return JsonDocument.Parse(await page.Content.ReadAsStringAsync(Ct)).RootElement;
    }

    // ---- a grant only works on its own tenant's Hub --------------------------

    [Fact]
    public async Task A_refresh_for_another_tenants_grant_is_refused()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        app.Services.GetRequiredService<IAgentAccessTenantResolver>()
            .ResolveAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentAccessTenant(99, "other", "Other Couriers"));

        await AssertInvalidGrant(await Refresh(app, tokens.Refresh));
    }
}
