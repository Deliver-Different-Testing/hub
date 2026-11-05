using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Keys;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Gate;

// Agent access gate. Enabled only when the tenant switch is on, DF Admin has not forced it
// off, and the OAuth keys are persistent. The Feature matrix and the tenant lifecycle are not
// consulted. Anything unknown, missing or failing means NOT enabled.
public class AgentAccessGateTests
{
    // ---- the three conditions, every combination --------------------------

    public static TheoryData<bool, bool, bool> Combinations()
    {
        var data = new TheoryData<bool, bool, bool>();
        foreach (var switchOn in new[] { true, false })
        foreach (var forcedOff in new[] { true, false })
        foreach (var keysPersistent in new[] { true, false })
        {
            data.Add(switchOn, forcedOff, keysPersistent);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Enabled_only_when_switch_on_not_forced_off_and_keys_persistent(
        bool switchOn, bool forcedOff, bool keysPersistent)
    {
        var decision = await Gate(switchOn, forcedOff, keysPersistent).EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(switchOn && !forcedOff && keysPersistent, decision.Enabled);

        // First failing condition wins: forced off, keys, switch.
        var expectedReason =
            forcedOff ? AgentAccessReason.ForcedOff
            : !keysPersistent ? AgentAccessReason.KeysNotPersistent
            : !switchOn ? AgentAccessReason.TenantSwitchOff
            : AgentAccessReason.Enabled;
        Assert.Equal(expectedReason, decision.Reason);
    }

    // ---- fail closed ------------------------------------------------------

    [Fact]
    public async Task Tenant_settings_failure_is_not_enabled()
    {
        var settings = Substitute.For<ITenantAgentSettingSource>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns<TenantAgentSettingRow?>(_ => throw new InvalidOperationException("Invalid column name"));

        var decision = await new AgentAccessGate(settings, OkKeys())
            .EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.False(decision.Enabled);
        Assert.Equal(AgentAccessReason.Error, decision.Reason);
    }

    [Fact]
    public async Task A_missing_settings_row_reads_as_the_switch_being_off()
    {
        var settings = Substitute.For<ITenantAgentSettingSource>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns((TenantAgentSettingRow?)null);

        var decision = await new AgentAccessGate(settings, OkKeys())
            .EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.False(decision.Enabled);
        Assert.Equal(AgentAccessReason.TenantSwitchOff, decision.Reason);
    }

    [Fact]
    public async Task Each_evaluation_reads_the_tenant_settings_once()
    {
        var settings = OkSettings();

        await new AgentAccessGate(settings, OkKeys()).EvaluateAsync(TestContext.Current.CancellationToken);

        await settings.Received(1).GetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_being_swallowed_as_an_error()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var settings = Substitute.For<ITenantAgentSettingSource>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns<TenantAgentSettingRow?>(_ => throw new OperationCanceledException(cts.Token));

        var gate = new AgentAccessGate(settings, OkKeys());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.EvaluateAsync(cts.Token));
    }

    [Fact]
    public async Task Forced_off_wins_even_with_the_switch_on()
    {
        var decision = await Gate(switchOn: true, forcedOff: true).EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.False(decision.Enabled);
        Assert.Equal(AgentAccessReason.ForcedOff, decision.Reason);
    }

    [Fact]
    public async Task Non_persistent_keys_keep_the_gate_closed_even_when_everything_else_is_on()
    {
        var decision = await Gate(keysPersistent: false).EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.False(decision.Enabled);
        Assert.Equal(AgentAccessReason.KeysNotPersistent, decision.Reason);
    }

    [Fact]
    public async Task Key_state_failure_is_not_enabled()
    {
        var keys = Substitute.For<IAgentAccessKeyState>();
        keys.Persistent.Returns(_ => throw new InvalidOperationException("boom"));

        var decision = await new AgentAccessGate(OkSettings(), keys)
            .EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.False(decision.Enabled);
        Assert.Equal(AgentAccessReason.Error, decision.Reason);
    }

    // ---- helpers ----------------------------------------------------------

    private static AgentAccessGate Gate(bool switchOn = true, bool forcedOff = false, bool keysPersistent = true)
    {
        var settings = Substitute.For<ITenantAgentSettingSource>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new TenantAgentSettingRow(switchOn, forcedOff));

        return new AgentAccessGate(settings, Keys(keysPersistent));
    }

    private static IAgentAccessKeyState Keys(bool persistent) =>
        new AgentAccessKeyState(persistent, persistent ? "ok" : "not persistent", persistent ? "kid" : null);

    private static IAgentAccessKeyState OkKeys() => Keys(true);

    private static ITenantAgentSettingSource OkSettings()
    {
        var settings = Substitute.For<ITenantAgentSettingSource>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new TenantAgentSettingRow(SwitchOn: true, ForcedOff: false));
        return settings;
    }
}
