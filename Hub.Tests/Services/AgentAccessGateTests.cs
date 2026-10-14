using Hub.Interfaces;
using Hub.Models;
using Hub.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Hub.Tests.Services;

// Agent access gate (Agent Access spec v4, phase 1.1 / D19). Enabled only when the
// agent-access Feature row is Ready or Live, the tenant switch is on, the tenant
// lifecycle is Live or Demo, and DF Admin has not forced it off. Anything unknown,
// missing or failing means NOT enabled.
public class AgentAccessGateTests
{
    private const string Released = "Ready";
    private const string NotReleased = "Draft";

    // ---- the four conditions, every combination ---------------------------

    public static TheoryData<bool, bool, bool, bool> Combinations()
    {
        var data = new TheoryData<bool, bool, bool, bool>();
        foreach (var released in new[] { true, false })
        foreach (var lifecycleOk in new[] { true, false })
        foreach (var switchOn in new[] { true, false })
        foreach (var forcedOff in new[] { true, false })
        {
            data.Add(released, lifecycleOk, switchOn, forcedOff);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Enabled_only_when_all_four_conditions_hold(
        bool released, bool lifecycleOk, bool switchOn, bool forcedOff)
    {
        var gate = Gate(
            release: released ? Released : NotReleased,
            lifecycle: lifecycleOk ? TenantLifecycleState.Live : TenantLifecycleState.Suspended,
            switchOn: switchOn,
            forcedOff: forcedOff);

        var decision = await gate.EvaluateAsync(TestContext.Current.CancellationToken);

        var expectedEnabled = released && lifecycleOk && switchOn && !forcedOff;
        Assert.Equal(expectedEnabled, decision.Enabled);

        // First failing condition wins: forced off, release, lifecycle, switch.
        var expectedReason =
            forcedOff ? AgentAccessReason.ForcedOff
            : !released ? AgentAccessReason.FeatureNotReleased
            : !lifecycleOk ? AgentAccessReason.LifecycleNotAllowed
            : !switchOn ? AgentAccessReason.TenantSwitchOff
            : AgentAccessReason.Enabled;
        Assert.Equal(expectedReason, decision.Reason);
    }

    // ---- release state ----------------------------------------------------

    [Theory]
    [InlineData("Ready", true)]
    [InlineData("Live", true)]
    [InlineData("live", true)]      // the DB collation is case-insensitive; do not hide a Live row over casing
    [InlineData("Draft", false)]
    [InlineData("Internal Test", false)]
    [InlineData("Beta", false)]     // not a valid state in CK_Feature_ReleaseStatus; must never pass
    [InlineData("", false)]
    public async Task Release_status_gate(string status, bool expected)
    {
        var decision = await Gate(release: status).EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, decision.Enabled);
        if (!expected) Assert.Equal(AgentAccessReason.FeatureNotReleased, decision.Reason);
    }

    [Fact]
    public async Task Missing_feature_row_is_not_released()
    {
        var decision = await Gate(release: null).EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.False(decision.Enabled);
        Assert.Equal(AgentAccessReason.FeatureNotReleased, decision.Reason);
    }

    // ---- lifecycle --------------------------------------------------------

    [Theory]
    [InlineData(TenantLifecycleState.Live, true)]
    [InlineData(TenantLifecycleState.Demo, true)]
    [InlineData(TenantLifecycleState.Provisioned, false)]
    [InlineData(TenantLifecycleState.Configuring, false)]
    [InlineData(TenantLifecycleState.PreLiveUat, false)]
    [InlineData(TenantLifecycleState.ReadyForGoLive, false)]
    [InlineData(TenantLifecycleState.Suspended, false)]
    public async Task Lifecycle_gate(TenantLifecycleState state, bool expected)
    {
        var decision = await Gate(lifecycle: state).EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, decision.Enabled);
        if (!expected) Assert.Equal(AgentAccessReason.LifecycleNotAllowed, decision.Reason);
    }

    [Fact]
    public async Task Unknown_lifecycle_is_not_enabled()
    {
        var decision = await Gate(lifecycle: TenantLifecycleState.Unknown)
            .EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.False(decision.Enabled);
        Assert.Equal(AgentAccessReason.LifecycleUnknown, decision.Reason);
    }

    // ---- fail closed ------------------------------------------------------

    [Fact]
    public async Task Feature_reader_failure_is_not_enabled()
    {
        var features = Substitute.For<IAgentAccessFeatureReader>();
        features.GetReleaseStatusAsync(Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new InvalidOperationException("db down"));

        var decision = await new AgentAccessGate(features, OkState(), OkLifecycle())
            .EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.False(decision.Enabled);
        Assert.Equal(AgentAccessReason.Error, decision.Reason);
    }

    [Fact]
    public async Task Tenant_state_failure_is_not_enabled()
    {
        var state = Substitute.For<IAgentAccessTenantState>();
        state.GetAsync(Arg.Any<CancellationToken>())
            .Returns<AgentAccessTenantState>(_ => throw new InvalidOperationException("db down"));

        var decision = await new AgentAccessGate(OkFeatures(), state, OkLifecycle())
            .EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.False(decision.Enabled);
        Assert.Equal(AgentAccessReason.Error, decision.Reason);
    }

    [Fact]
    public async Task Lifecycle_reader_failure_is_not_enabled()
    {
        var lifecycle = Substitute.For<ITenantLifecycleReader>();
        lifecycle.GetAsync(Arg.Any<CancellationToken>())
            .Returns<TenantLifecycleState>(_ => throw new InvalidOperationException("db down"));

        var decision = await new AgentAccessGate(OkFeatures(), OkState(), lifecycle)
            .EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.False(decision.Enabled);
        Assert.Equal(AgentAccessReason.Error, decision.Reason);
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_being_swallowed_as_an_error()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var features = Substitute.For<IAgentAccessFeatureReader>();
        features.GetReleaseStatusAsync(Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new OperationCanceledException(cts.Token));

        var gate = new AgentAccessGate(features, OkState(), OkLifecycle());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.EvaluateAsync(cts.Token));
    }

    [Fact]
    public async Task Forced_off_short_circuits_without_reading_the_other_inputs()
    {
        var features = Substitute.For<IAgentAccessFeatureReader>();
        var lifecycle = Substitute.For<ITenantLifecycleReader>();
        var state = Substitute.For<IAgentAccessTenantState>();
        state.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessTenantState(SwitchOn: true, ForcedOff: true));

        var decision = await new AgentAccessGate(features, state, lifecycle)
            .EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AgentAccessReason.ForcedOff, decision.Reason);
        await features.DidNotReceive().GetReleaseStatusAsync(Arg.Any<CancellationToken>());
        await lifecycle.DidNotReceive().GetAsync(Arg.Any<CancellationToken>());
    }

    // ---- defaults until the real stores land -------------------------------

    [Fact]
    public async Task Default_tenant_state_is_switch_off_and_not_forced()
    {
        var state = await new AgentAccessTenantStateNotConfigured().GetAsync(TestContext.Current.CancellationToken);

        Assert.False(state.SwitchOn);
        Assert.False(state.ForcedOff);
    }

    [Fact]
    public async Task Default_lifecycle_reader_is_unknown()
    {
        var state = await new TenantLifecycleNotConfigured().GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TenantLifecycleState.Unknown, state);
    }

    [Fact]
    public async Task Gate_with_the_default_ports_is_never_enabled_even_when_the_feature_is_live()
    {
        var gate = new AgentAccessGate(
            OkFeatures(), new AgentAccessTenantStateNotConfigured(), new TenantLifecycleNotConfigured());

        var decision = await gate.EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.False(decision.Enabled);
    }

    // ---- the real feature reader --------------------------------------------

    [Fact]
    public async Task Feature_reader_returns_null_when_the_row_is_missing()
    {
        await using var ctx = NewContext(nameof(Feature_reader_returns_null_when_the_row_is_missing));
        AddFeature(ctx, "hub-tile-other", "Live");
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var status = await new AgentAccessFeatureReader(ctx).GetReleaseStatusAsync(TestContext.Current.CancellationToken);

        Assert.Null(status);
    }

    [Fact]
    public async Task Feature_reader_returns_only_the_agent_access_status()
    {
        await using var ctx = NewContext(nameof(Feature_reader_returns_only_the_agent_access_status));
        AddFeature(ctx, "hub-tile-other", "Live");
        AddFeature(ctx, AgentAccessGate.FeatureKey, "Ready");
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var status = await new AgentAccessFeatureReader(ctx).GetReleaseStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Ready", status);
    }

    [Fact]
    public async Task Feature_reader_does_not_depend_on_client_visible()
    {
        // FeatureResolver would hide this row from everyone (ClientVisible = false);
        // the gate reads the release state directly so a Ready row still counts.
        await using var ctx = NewContext(nameof(Feature_reader_does_not_depend_on_client_visible));
        AddFeature(ctx, AgentAccessGate.FeatureKey, "Ready", clientVisible: false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var status = await new AgentAccessFeatureReader(ctx).GetReleaseStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Ready", status);
    }

    // ---- helpers ----------------------------------------------------------

    private static AgentAccessGate Gate(
        string? release = Released,
        TenantLifecycleState lifecycle = TenantLifecycleState.Live,
        bool switchOn = true,
        bool forcedOff = false)
    {
        var features = Substitute.For<IAgentAccessFeatureReader>();
        features.GetReleaseStatusAsync(Arg.Any<CancellationToken>()).Returns(release);

        var state = Substitute.For<IAgentAccessTenantState>();
        state.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessTenantState(switchOn, forcedOff));

        var lifecycleReader = Substitute.For<ITenantLifecycleReader>();
        lifecycleReader.GetAsync(Arg.Any<CancellationToken>()).Returns(lifecycle);

        return new AgentAccessGate(features, state, lifecycleReader);
    }

    private static IAgentAccessFeatureReader OkFeatures()
    {
        var features = Substitute.For<IAgentAccessFeatureReader>();
        features.GetReleaseStatusAsync(Arg.Any<CancellationToken>()).Returns("Live");
        return features;
    }

    private static IAgentAccessTenantState OkState()
    {
        var state = Substitute.For<IAgentAccessTenantState>();
        state.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessTenantState(SwitchOn: true, ForcedOff: false));
        return state;
    }

    private static ITenantLifecycleReader OkLifecycle()
    {
        var lifecycle = Substitute.For<ITenantLifecycleReader>();
        lifecycle.GetAsync(Arg.Any<CancellationToken>()).Returns(TenantLifecycleState.Live);
        return lifecycle;
    }

    private static void AddFeature(DynamicDespatchDbContext ctx, string key, string status, bool clientVisible = true)
    {
        ctx.Features.Add(new Feature
        {
            FeatureKey = key,
            DisplayName = key,
            Description = "test",
            Category = "HubTile",
            ClientVisible = clientVisible,
            ReleaseStatus = status
        });
    }

    private static DynamicDespatchDbContext NewContext(string name)
    {
        var options = new DbContextOptionsBuilder<DespatchContext>()
            .UseInMemoryDatabase(name)
            .Options;

        // Never consulted: OnConfiguring short-circuits when options are already set.
        return new DynamicDespatchDbContext(options, Substitute.For<IConnectionStringManager>());
    }
}
