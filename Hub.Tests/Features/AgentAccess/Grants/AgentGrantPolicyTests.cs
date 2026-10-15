using Hub.Features.AgentAccess;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Tests.Features.AgentAccess;

// Phase 1.6: the rules a refresh has to pass, with no HTTP, Redis or database involved.
public class AgentGrantPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private static AgentGrantState State(
        string status = Statuses.Valid,
        DateTimeOffset? created = null,
        bool tenantOff = false,
        AgentGrantEligibility eligibility = AgentGrantEligibility.Eligible) =>
        new(status, created ?? Now.AddDays(-10), tenantOff, eligibility);

    [Fact]
    public void A_valid_recent_grant_for_an_eligible_user_may_refresh()
    {
        Assert.Equal(AgentGrantVerdict.Allow, AgentGrantPolicy.Decide(State(), Now).Verdict);
    }

    [Theory]
    [InlineData(Statuses.Revoked)]
    [InlineData(Statuses.Inactive)]
    [InlineData("")]
    public void A_grant_that_is_not_valid_is_refused_without_further_changes(string status)
    {
        var decision = AgentGrantPolicy.Decide(State(status: status), Now);

        Assert.Equal(AgentGrantVerdict.Refuse, decision.Verdict);
        Assert.False(decision.RevokeGrant);
    }

    [Fact]
    public void A_grant_without_a_creation_date_is_refused_and_revoked()
    {
        var decision = AgentGrantPolicy.Decide(new AgentGrantState(Statuses.Valid, null, false, AgentGrantEligibility.Eligible), Now);

        Assert.Equal(AgentGrantVerdict.Refuse, decision.Verdict);
        Assert.True(decision.RevokeGrant);
    }

    [Theory]
    [InlineData(364, true)]
    [InlineData(365, false)]
    [InlineData(366, false)]
    [InlineData(800, false)]
    public void A_grant_cannot_outlive_twelve_months_and_is_revoked_when_it_does(int ageDays, bool allowed)
    {
        var decision = AgentGrantPolicy.Decide(State(created: Now.AddDays(-ageDays)), Now);

        Assert.Equal(allowed, decision.Verdict == AgentGrantVerdict.Allow);
        Assert.Equal(!allowed, decision.RevokeGrant);
        if (!allowed) Assert.Equal(Errors.InvalidGrant, decision.Error);
    }

    [Fact]
    public void A_future_creation_date_is_not_trusted()
    {
        var decision = AgentGrantPolicy.Decide(State(created: Now.AddDays(1)), Now);

        Assert.Equal(AgentGrantVerdict.Refuse, decision.Verdict);
        Assert.True(decision.RevokeGrant);
    }

    [Theory]
    [InlineData(AgentGrantEligibility.NoLongerEligible)]
    [InlineData(AgentGrantEligibility.Unknown)]
    public void A_user_who_is_no_longer_eligible_or_cannot_be_checked_does_not_refresh(AgentGrantEligibility eligibility)
    {
        var decision = AgentGrantPolicy.Decide(State(eligibility: eligibility), Now);

        Assert.Equal(AgentGrantVerdict.Refuse, decision.Verdict);
        Assert.Equal(Errors.InvalidGrant, decision.Error);
    }

    [Fact]
    public void A_user_who_is_definitely_no_longer_eligible_has_the_grant_revoked_but_an_unknown_one_does_not()
    {
        Assert.True(AgentGrantPolicy.Decide(State(eligibility: AgentGrantEligibility.NoLongerEligible), Now).RevokeGrant);
        Assert.False(AgentGrantPolicy.Decide(State(eligibility: AgentGrantEligibility.Unknown), Now).RevokeGrant);
    }

    [Fact]
    public void A_tenant_that_is_switched_off_in_its_own_database_refuses_but_keeps_the_grant()
    {
        var decision = AgentGrantPolicy.Decide(State(eligibility: AgentGrantEligibility.TenantOff), Now);

        Assert.Equal(AgentGrantVerdict.Refuse, decision.Verdict);
        Assert.False(decision.RevokeGrant);
        Assert.Equal(Errors.InvalidGrant, decision.Error);
    }

    [Fact]
    public void The_tenant_off_flag_refuses_but_keeps_the_grant_so_it_resumes_when_switched_back_on()
    {
        var decision = AgentGrantPolicy.Decide(State(tenantOff: true), Now);

        Assert.Equal(AgentGrantVerdict.Refuse, decision.Verdict);
        Assert.False(decision.RevokeGrant);
    }

    [Fact]
    public void Eligibility_is_not_even_needed_when_the_grant_is_already_dead()
    {
        var decision = AgentGrantPolicy.Decide(State(status: Statuses.Revoked, eligibility: AgentGrantEligibility.NoLongerEligible), Now);

        Assert.False(decision.RevokeGrant);
    }
}
