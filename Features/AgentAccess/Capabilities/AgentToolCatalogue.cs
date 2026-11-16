namespace Hub.Features.AgentAccess.Capabilities;

/// <summary>An MCP tool, the verb that switches it on, the role actions it also needs inside the tile (all of them) and the hub tile they sit under.</summary>
public sealed record AgentToolDefinition(string Tool, string Verb, IReadOnlyList<string> ScreenKeys, string HubTile);

// MIRROR of DeliverDifferent.AgentAccess.AgentToolCatalogue (dfrnt-automate-mcp). Hub has no reference to that package
// and its NuGet.Config only has nuget.org, so the rows are copied here; AgentToolCatalogueTests pins every row so drift
// from the package's table fails the build when either side changes. Keep the order the same as the package.
//
// AI tools gate on the hub tile plus role actions only. The dw-* menu screens are deliberately NOT checked (Jacob,
// 2026-11-12): under the tile model a role row on a dw-* key changes nothing, and the configurator no longer lets
// anyone set one per role. Configurator tools list feature keys (cfg-*, reports, multi-client ...) as their role actions.
public static class AgentToolCatalogue
{
    public const string DespatchTile = "hub-tile-despatchweb";
    public const string ConfiguratorTile = "hub-tile-configurator";

    public static readonly IReadOnlyList<AgentToolDefinition> Tools =
    [
        new("job_search", "agent-tool-job-search", [], DespatchTile),
        new("job_story", "agent-tool-job-story", [], DespatchTile),
        new("job_evidence", "agent-tool-job-evidence", [], DespatchTile),
        new("recurring_check", "agent-tool-recurring-check", [], DespatchTile),
        new("partner_inbox", "agent-tool-partner-inbox", [], DespatchTile),
        new("dispatch_board", "agent-tool-dispatch-board", ["view-dispatch-board"], DespatchTile),
        new("driver_day", "agent-tool-driver-day", [], DespatchTile),
        new("automation_engine_pulse", "agent-tool-automation-pulse", ["cfg-automations"], ConfiguratorTile),
        new("automation_firing_census", "agent-tool-automation-census", ["cfg-automations"], ConfiguratorTile),
        new("automation_job_trail", "agent-tool-automation-job-trail", ["cfg-automations"], ConfiguratorTile),
        new("rule_readback", "agent-tool-rule-readback", ["cfg-automations"], ConfiguratorTile),
        new("client_month_report", "agent-tool-client-month-report", ["reports"], ConfiguratorTile),
        new("courier_day_report", "agent-tool-courier-day-report", ["reports"], ConfiguratorTile),
        new("fleet_compliance", "agent-tool-fleet-compliance", ["cfg-fleet", "courier-compliance-monitoring"], ConfiguratorTile),
        new("compliance_chase_history", "agent-tool-compliance-chase-history", ["cfg-fleet", "courier-compliance-monitoring"], ConfiguratorTile),
        new("agent_onboarding_status", "agent-tool-agent-onboarding-status", ["multi-client", "courier-compliance-monitoring"], ConfiguratorTile),
        new("access_explain", "agent-tool-access-explain", ["cfg-settings-tile-access"], ConfiguratorTile),
        new("rate_change_history", "agent-tool-rate-change-history", ["cfg-pricing"], ConfiguratorTile)
    ];
}
