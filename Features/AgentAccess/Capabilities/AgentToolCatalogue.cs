namespace Hub.Features.AgentAccess.Capabilities;

/// <summary>An MCP tool, the verb that switches it on, the role actions it also needs inside the tile (all of them) and the hub tile they sit under.</summary>
public sealed record AgentToolDefinition(string Tool, string Verb, IReadOnlyList<string> ScreenKeys, string HubTile);

// MIRROR of DeliverDifferent.AgentAccess.AgentToolCatalogue (dfrnt-automate-mcp). Hub has no reference to that package
// and its NuGet.Config only has nuget.org, so the rows are copied here; AgentToolCatalogueTests pins every row so drift
// from the package's table fails the build when either side changes. Keep the order the same as the package.
//
// AI tools gate on the hub tile plus role actions only. The dw-* menu screens are deliberately NOT checked (Jacob,
// 2026-11-12): under the tile model a role row on a dw-* key changes nothing, and the configurator no longer lets
// anyone set one per role.
public static class AgentToolCatalogue
{
    public const string DespatchTile = "hub-tile-despatchweb";

    public static readonly IReadOnlyList<AgentToolDefinition> Tools =
    [
        new("job_search", "agent-tool-job-search", [], DespatchTile),
        new("job_story", "agent-tool-job-story", [], DespatchTile),
        new("job_evidence", "agent-tool-job-evidence", [], DespatchTile),
        new("recurring_check", "agent-tool-recurring-check", [], DespatchTile),
        new("partner_inbox", "agent-tool-partner-inbox", [], DespatchTile),
        new("dispatch_board", "agent-tool-dispatch-board", ["view-dispatch-board"], DespatchTile),
        new("driver_day", "agent-tool-driver-day", [], DespatchTile)
    ];
}
