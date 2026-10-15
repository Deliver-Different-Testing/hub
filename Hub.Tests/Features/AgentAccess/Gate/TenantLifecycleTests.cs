using Hub.Features.AgentAccess;

namespace Hub.Tests.Features.AgentAccess;

public class TenantLifecycleTests
{
    [Theory]
    [InlineData("Live", TenantLifecycleState.Live)]
    [InlineData("Demo", TenantLifecycleState.Demo)]
    [InlineData("live", TenantLifecycleState.Live)]
    [InlineData(" Live ", TenantLifecycleState.Live)]
    [InlineData("PreLiveUat", TenantLifecycleState.PreLiveUat)]
    [InlineData("Suspended", TenantLifecycleState.Suspended)]
    [InlineData("Provisioned", TenantLifecycleState.Provisioned)]
    public void A_named_state_is_read(string stored, TenantLifecycleState expected)
    {
        Assert.Equal(expected, TenantLifecycle.Parse(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Beta")]
    [InlineData("3")]
    [InlineData("Unknown")]
    public void Missing_numeric_or_unlisted_values_are_unknown(string? stored)
    {
        Assert.Equal(TenantLifecycleState.Unknown, TenantLifecycle.Parse(stored));
    }

    [Theory]
    [InlineData("Live", true)]
    [InlineData("demo", true)]
    [InlineData("Configuring", false)]
    [InlineData("Suspended", false)]
    [InlineData(null, false)]
    public void Only_live_and_demo_allow_agent_access(string? stored, bool expected)
    {
        Assert.Equal(expected, TenantLifecycle.AllowsAgentAccess(stored));
    }
}
