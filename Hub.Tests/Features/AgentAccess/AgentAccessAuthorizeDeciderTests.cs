using Hub.Services;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Hub.Tests.Services;
using Hub.Features.AgentAccess;

namespace Hub.Tests.Features.AgentAccess;

// Phase 1.5: the rules behind /connect/authorize, with no HTTP or database involved.
public class AgentAccessAuthorizeDeciderTests
{
    private const string Resource = "https://mcp.urgent.example.test/mcp";

    private static AgentAccessUser User(int? clientType = 4, int? tenantId = 12, string userId = "55") =>
        new(userId, StaffId: 7, ContactId: 9, ClientTypeId: clientType, TenantId: tenantId);

    private static AgentAccessAuthorizeInput Input(
        string[]? scopes = null, string[]? resources = null, string[]? prompts = null, bool existing = false) =>
        new(scopes ?? ["mcp", "offline_access"], resources ?? [Resource], prompts ?? [], existing);

    private static string[] Split(string joined) => joined.Length == 0 ? [] : joined.Split('|');

    private static AgentAccessAuthorizeDecision Decide(
        AgentAccessUser? user, AgentAccessAuthorizeInput input, string? resource = Resource) =>
        AgentAccessAuthorizeDecider.Decide(user, input, resource);

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Tenant_staff_and_df_admin_are_asked_for_consent(int clientType)
    {
        var decision = Decide(User(clientType), Input());

        Assert.Equal(AgentAccessAuthorizeOutcome.NeedsConsent, decision.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void Other_client_types_and_couriers_are_denied(int? clientType)
    {
        var decision = Decide(User(clientType), Input());

        Assert.Equal(AgentAccessAuthorizeOutcome.Reject, decision.Outcome);
        Assert.Equal(Errors.AccessDenied, decision.Error);
    }

    [Fact]
    public void A_cookie_without_a_tenant_or_user_is_denied()
    {
        Assert.Equal(Errors.AccessDenied, Decide(User(tenantId: null), Input()).Error);
        Assert.Equal(Errors.AccessDenied, Decide(User(userId: ""), Input()).Error);
    }

    [Fact]
    public void Not_signed_in_goes_to_login()
    {
        var decision = Decide(null, Input());

        Assert.Equal(AgentAccessAuthorizeOutcome.LoginRequired, decision.Outcome);
    }

    [Fact]
    public void Not_signed_in_with_prompt_none_is_login_required_error()
    {
        var decision = Decide(null, Input(prompts: ["none"]));

        Assert.Equal(AgentAccessAuthorizeOutcome.Reject, decision.Outcome);
        Assert.Equal(Errors.LoginRequired, decision.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://mcp.other.example.test/mcp")]
    [InlineData(Resource + "|" + Resource)]
    [InlineData(Resource + "|https://evil.example.test/mcp")]
    [InlineData("https://mcp.urgent.example.test/mcp/")]
    [InlineData("HTTPS://MCP.URGENT.EXAMPLE.TEST/mcp")]
    public void Resource_must_be_exactly_this_tenants_mcp_url(string resources)
    {
        var decision = Decide(User(), Input(resources: Split(resources)));

        Assert.Equal(AgentAccessAuthorizeOutcome.Reject, decision.Outcome);
        Assert.Equal(Errors.InvalidTarget, decision.Error);
    }

    [Fact]
    public void Resource_is_checked_before_login_so_a_bad_request_fails_fast()
    {
        var decision = Decide(null, Input(resources: ["https://evil.example.test/mcp"]));

        Assert.Equal(Errors.InvalidTarget, decision.Error);
    }

    [Fact]
    public void Every_request_is_rejected_when_the_tenant_has_no_resource()
    {
        var decision = Decide(User(), Input(), resource: null);

        Assert.Equal(Errors.InvalidTarget, decision.Error);
    }

    [Theory]
    [InlineData("offline_access")]
    [InlineData("mcp|admin")]
    [InlineData("openid")]
    [InlineData("")]
    public void Scopes_must_include_mcp_and_nothing_outside_the_allow_list(string scopes)
    {
        var decision = Decide(User(), Input(scopes: Split(scopes)));

        Assert.Equal(AgentAccessAuthorizeOutcome.Reject, decision.Outcome);
        Assert.Equal(Errors.InvalidScope, decision.Error);
    }

    [Fact]
    public void Mcp_alone_is_allowed()
    {
        Assert.Equal(AgentAccessAuthorizeOutcome.NeedsConsent, Decide(User(), Input(scopes: ["mcp"])).Outcome);
    }

    [Fact]
    public void An_existing_permanent_authorization_skips_consent()
    {
        var decision = Decide(User(), Input(existing: true));

        Assert.Equal(AgentAccessAuthorizeOutcome.Issue, decision.Outcome);
    }

    [Fact]
    public void Prompt_consent_forces_the_consent_page_even_with_an_authorization()
    {
        var decision = Decide(User(), Input(existing: true, prompts: ["consent"]));

        Assert.Equal(AgentAccessAuthorizeOutcome.NeedsConsent, decision.Outcome);
    }

    [Fact]
    public void Prompt_none_without_an_authorization_is_consent_required()
    {
        var decision = Decide(User(), Input(prompts: ["none"]));

        Assert.Equal(AgentAccessAuthorizeOutcome.Reject, decision.Outcome);
        Assert.Equal(Errors.ConsentRequired, decision.Error);
    }

    [Fact]
    public void Prompt_none_with_an_authorization_issues_silently()
    {
        var decision = Decide(User(), Input(existing: true, prompts: ["none"]));

        Assert.Equal(AgentAccessAuthorizeOutcome.Issue, decision.Outcome);
    }

    [Fact]
    public void An_ineligible_user_never_gets_a_code_even_with_an_old_authorization()
    {
        var decision = Decide(User(clientType: 2), Input(existing: true));

        Assert.Equal(Errors.AccessDenied, decision.Error);
    }
}
