using Hub.Features.AgentAccess.Capabilities;
using Hub.Interfaces;

namespace Hub.Tests.Features.AgentAccess.Capabilities;

// Tool verbs: granted only when the verb, the hub tile and every role-action key the tool lists all pass (level 1 or
// more; no explicit row on the key or an ancestor falls back to the tile decision; explicit None denies). dw-* menu
// screens are deliberately not checked (Jacob, 2026-11-12).
public partial class AgentCapabilityResolverTests
{
    private static readonly string[] JobSearchTools =
        ["agent-tool-job-search", "agent-tool-job-story", "agent-tool-job-evidence"];

    private static string[] ToolVerbs => AgentToolCatalogue.Tools.Select(t => t.Verb).ToArray();

    private static void GrantTools(DynamicDespatchDbContext ctx, int roleId)
    {
        DenyAllVerbs(ctx, roleId);
        Set(ctx, roleId, AgentCapabilities.Connect, 1);
        foreach (var verb in ToolVerbs) Set(ctx, roleId, verb, 1);
    }

    [Fact]
    public async Task Tools_with_no_screen_rows_follow_the_despatch_tile()
    {
        await using var ctx = NewContext(nameof(Tools_with_no_screen_rows_follow_the_despatch_tile));
        Seed(ctx, roles: [RoleA]);
        GrantTools(ctx, RoleA);
        await ctx.SaveChangesAsync(Ct);

        var result = await ResolveDetailed(ctx);

        Assert.Equal(ToolVerbs.Append(AgentCapabilities.Connect).Order(), result.Capabilities.Order());
        Assert.All(result.Tools.Values, t => Assert.Equal(new AgentToolDecision(true, true, null), t));
    }

    [Fact]
    public async Task A_tile_the_tenant_does_not_have_blocks_the_tools_with_a_reason()
    {
        await using var ctx = NewContext(nameof(A_tile_the_tenant_does_not_have_blocks_the_tools_with_a_reason));
        Seed(ctx, roles: [RoleA]);
        GrantTools(ctx, RoleA);
        await ctx.SaveChangesAsync(Ct);

        var result = await ResolveDetailed(ctx, enabledTiles: [IntegrationManager, Configurator]);

        Assert.Equal([AgentCapabilities.Connect], result.Capabilities);
        Assert.All(result.Tools.Values, t => Assert.Equal(new AgentToolDecision(true, false, "Despatch Web tile is off"), t));
    }

    [Fact]
    public async Task An_explicit_none_on_the_despatch_tile_denies_every_tool()
    {
        await using var ctx = NewContext(nameof(An_explicit_none_on_the_despatch_tile_denies_every_tool));
        Seed(ctx, roles: [RoleA]);
        GrantTools(ctx, RoleA);
        Set(ctx, RoleA, IntegrationManager, 1);
        Set(ctx, RoleA, DespatchWeb, 0);
        await ctx.SaveChangesAsync(Ct);

        var result = await ResolveDetailed(ctx);

        Assert.Equal([AgentCapabilities.Connect], result.Capabilities);
        Assert.All(result.Tools.Values, t => Assert.False(t.Screen));
    }

    [Fact]
    public async Task A_configured_role_holding_the_tile_with_no_screen_rows_keeps_the_tools()
    {
        await using var ctx = NewContext(nameof(A_configured_role_holding_the_tile_with_no_screen_rows_keeps_the_tools));
        Seed(ctx, roles: [RoleA]);
        GrantTools(ctx, RoleA);
        Set(ctx, RoleA, IntegrationManager, 1);
        Set(ctx, RoleA, DespatchWeb, 1);
        Set(ctx, RoleA, Configurator, 0);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ToolVerbs.Append(AgentCapabilities.Connect).Order(), (await Resolve(ctx)).Order());
    }

    [Fact]
    public async Task An_explicit_none_on_a_dw_menu_key_no_longer_blocks_any_tool()
    {
        await using var ctx = NewContext(nameof(An_explicit_none_on_a_dw_menu_key_no_longer_blocks_any_tool));
        Seed(ctx, roles: [RoleA]);
        GrantTools(ctx, RoleA);
        Set(ctx, RoleA, "dw-job-search", 0);
        Set(ctx, RoleA, "dw-recurring-jobs", 0);
        Set(ctx, RoleA, "dw-driver-management", 0);
        await ctx.SaveChangesAsync(Ct);

        var result = await ResolveDetailed(ctx);

        foreach (var verb in JobSearchTools) Assert.Contains(verb, result.Capabilities);
        Assert.Contains("agent-tool-recurring-check", result.Capabilities);
        Assert.Contains("agent-tool-driver-day", result.Capabilities);
        Assert.All(result.Tools.Values, t => Assert.Equal(new AgentToolDecision(true, true, null), t));
    }

    [Fact]
    public async Task A_tool_verb_that_is_off_is_not_granted_even_when_the_tile_is_on()
    {
        await using var ctx = NewContext(nameof(A_tool_verb_that_is_off_is_not_granted_even_when_the_tile_is_on));
        Seed(ctx, roles: [RoleA]);
        GrantTools(ctx, RoleA);
        Set(ctx, RoleA, "agent-tool-job-story", 0);
        await ctx.SaveChangesAsync(Ct);

        var result = await ResolveDetailed(ctx);

        Assert.DoesNotContain("agent-tool-job-story", result.Capabilities);
        Assert.Equal(new AgentToolDecision(false, true, "agent-tool-job-story is off for this role"), result.Tools["agent-tool-job-story"]);
    }

    [Fact]
    public async Task A_per_client_override_on_the_tool_verb_beats_the_global_row()
    {
        await using var ctx = NewContext(nameof(A_per_client_override_on_the_tool_verb_beats_the_global_row));
        Seed(ctx, roles: [RoleA]);
        GrantTools(ctx, RoleA);
        Set(ctx, RoleA, "agent-tool-driver-day", 0, clientId: ClientId);
        await ctx.SaveChangesAsync(Ct);

        var result = await Resolve(ctx);

        Assert.DoesNotContain("agent-tool-driver-day", result);
        Assert.Contains("agent-tool-job-search", result);
    }

    [Fact]
    public async Task The_dispatch_board_needs_view_dispatch_board()
    {
        await using var ctx = NewContext(nameof(The_dispatch_board_needs_view_dispatch_board));
        Seed(ctx, roles: [RoleA]);
        GrantTools(ctx, RoleA);
        Set(ctx, RoleA, "view-dispatch-board", 0);
        await ctx.SaveChangesAsync(Ct);

        var result = await ResolveDetailed(ctx);
        Assert.DoesNotContain("agent-tool-dispatch-board", result.Capabilities);
        Assert.Equal(
            new AgentToolDecision(true, false, "view-dispatch-board is off for this role"),
            result.Tools["agent-tool-dispatch-board"]);

        Set(ctx, RoleA, "view-dispatch-board", 1);
        await ctx.SaveChangesAsync(Ct);
        Assert.Contains("agent-tool-dispatch-board", await Resolve(ctx));
    }

    [Fact]
    public async Task A_per_client_override_on_view_dispatch_board_beats_the_global_none()
    {
        await using var ctx = NewContext(nameof(A_per_client_override_on_view_dispatch_board_beats_the_global_none));
        Seed(ctx, roles: [RoleA]);
        GrantTools(ctx, RoleA);
        Set(ctx, RoleA, "view-dispatch-board", 0);
        Set(ctx, RoleA, "view-dispatch-board", 1, clientId: ClientId);
        await ctx.SaveChangesAsync(Ct);

        Assert.Contains("agent-tool-dispatch-board", await Resolve(ctx));
    }

    [Fact]
    public async Task Stacked_roles_union_view_dispatch_board()
    {
        await using var ctx = NewContext(nameof(Stacked_roles_union_view_dispatch_board));
        Seed(ctx, roles: [RoleA, RoleB]);
        GrantTools(ctx, RoleA);
        Set(ctx, RoleA, "view-dispatch-board", 0);
        Set(ctx, RoleB, "view-dispatch-board", 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Contains("agent-tool-dispatch-board", await Resolve(ctx));
    }

    [Fact]
    public async Task Data_classes_and_connection_management_are_independent_of_the_tools()
    {
        await using var ctx = NewContext(nameof(Data_classes_and_connection_management_are_independent_of_the_tools));
        Seed(ctx, roles: [RoleA]);
        DenyAllVerbs(ctx, RoleA);
        Set(ctx, RoleA, AgentCapabilities.Connect, 1);
        Set(ctx, RoleA, AgentCapabilities.SeeCosts, 1);
        Set(ctx, RoleA, AgentCapabilities.SeePod, 1);
        Set(ctx, RoleA, AgentCapabilities.ManageConnections, 1);
        await ctx.SaveChangesAsync(Ct);

        var result = await ResolveDetailed(ctx);

        Assert.Equal(
            new[] { AgentCapabilities.Connect, AgentCapabilities.SeeCosts, AgentCapabilities.SeePod, AgentCapabilities.ManageConnections }.Order(),
            result.Capabilities.Order());
        Assert.All(result.Tools.Values, t => Assert.False(t.Verb));
    }

    [Fact]
    public async Task Tools_are_not_granted_without_connect_but_still_report_their_decision()
    {
        await using var ctx = NewContext(nameof(Tools_are_not_granted_without_connect_but_still_report_their_decision));
        Seed(ctx, roles: [RoleA]);
        GrantTools(ctx, RoleA);
        Set(ctx, RoleA, AgentCapabilities.Connect, 0);
        await ctx.SaveChangesAsync(Ct);

        var result = await ResolveDetailed(ctx);

        Assert.Empty(result.Capabilities);
        Assert.Equal(ToolVerbs.Length, result.Tools.Count);
    }

    [Fact]
    public async Task DF_admin_passes_every_tool()
    {
        await using var ctx = NewContext(nameof(DF_admin_passes_every_tool));
        Seed(ctx, roles: [RoleA], clientTypeId: 5);
        DenyAllVerbs(ctx, RoleA);
        await ctx.SaveChangesAsync(Ct);

        var result = await ResolveDetailed(ctx, enabledTiles: []);

        Assert.Equal(ToolVerbs.Order(), result.Tools.Keys.Order());
        Assert.All(result.Tools.Values, t => Assert.Equal(new AgentToolDecision(true, true, null), t));
        Assert.Contains(AgentCapabilities.ManageConnections, result.Capabilities);
    }
}
