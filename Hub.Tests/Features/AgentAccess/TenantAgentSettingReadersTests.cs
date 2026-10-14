using Hub.Interfaces;
using Hub.Services;
using NSubstitute;
using Hub.Tests.Services;
using Hub.Features.AgentAccess;

namespace Hub.Tests.Features.AgentAccess;

// Phase 1.7: the gate's switch, force-off and lifecycle come from three columns on tblSetting
// (TenantAgentSettingRow). Anything missing or unrecognised keeps the gate closed.
public class TenantAgentSettingReadersTests
{
    private static ITenantAgentSettingSource Source(TenantAgentSettingRow? row)
    {
        var source = Substitute.For<ITenantAgentSettingSource>();
        source.GetAsync(Arg.Any<CancellationToken>()).Returns(row);
        return source;
    }

    [Theory]
    [InlineData("Live", TenantLifecycleState.Live)]
    [InlineData("Demo", TenantLifecycleState.Demo)]
    [InlineData("live", TenantLifecycleState.Live)]
    [InlineData(" Live ", TenantLifecycleState.Live)]
    [InlineData("PreLiveUat", TenantLifecycleState.PreLiveUat)]
    [InlineData("Suspended", TenantLifecycleState.Suspended)]
    [InlineData("Provisioned", TenantLifecycleState.Provisioned)]
    public async Task Lifecycle_reads_the_named_state(string stored, TenantLifecycleState expected)
    {
        var reader = new TenantLifecycleReader(Source(new TenantAgentSettingRow(stored, false, false)));

        Assert.Equal(expected, await reader.GetAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Beta")]
    [InlineData("3")]
    [InlineData("Unknown")]
    public async Task Lifecycle_is_unknown_for_missing_or_unrecognised_values(string? stored)
    {
        var reader = new TenantLifecycleReader(Source(new TenantAgentSettingRow(stored, true, false)));

        Assert.Equal(TenantLifecycleState.Unknown, await reader.GetAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Lifecycle_is_unknown_when_there_is_no_settings_row()
    {
        var reader = new TenantLifecycleReader(Source(null));

        Assert.Equal(TenantLifecycleState.Unknown, await reader.GetAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task State_reports_the_stored_switch_and_force_off(bool switchOn, bool forcedOff)
    {
        var reader = new AgentAccessTenantStateReader(Source(new TenantAgentSettingRow("Live", switchOn, forcedOff)));

        var state = await reader.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(switchOn, state.SwitchOn);
        Assert.Equal(forcedOff, state.ForcedOff);
    }

    [Fact]
    public async Task State_is_off_when_there_is_no_settings_row()
    {
        var reader = new AgentAccessTenantStateReader(Source(null));

        var state = await reader.GetAsync(TestContext.Current.CancellationToken);

        Assert.False(state.SwitchOn);
    }

    [Fact]
    public async Task Source_failure_propagates_so_the_gate_fails_closed()
    {
        var source = Substitute.For<ITenantAgentSettingSource>();
        source.GetAsync(Arg.Any<CancellationToken>()).Returns<Task<TenantAgentSettingRow?>>(_ => throw new InvalidOperationException("invalid column name"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new TenantLifecycleReader(source).GetAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentAccessTenantStateReader(source).GetAsync(TestContext.Current.CancellationToken));
    }
}
