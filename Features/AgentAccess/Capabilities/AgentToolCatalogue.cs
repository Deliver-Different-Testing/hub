namespace Hub.Features.AgentAccess.Capabilities;

/// <summary>An MCP tool, the verb that switches it on, the app screens it also needs (all of them) and the hub tile they sit under.</summary>
public sealed record AgentToolDefinition(string Tool, string Verb, IReadOnlyList<string> ScreenKeys, string HubTile);

// MIRROR of DeliverDifferent.AgentAccess.AgentToolCatalogue (dfrnt-automate-mcp). Hub has no reference to that package
// and its NuGet.Config only has nuget.org, so the rows are copied here; AgentToolCatalogueTests pins every row so drift
// from the package's table fails the build when either side changes. Keep the order the same as the package.
public static class AgentToolCatalogue
{
    public const string DespatchTile = "hub-tile-despatchweb";
    private const string JobSearch = "dw-job-search";

    public static readonly IReadOnlyList<AgentToolDefinition> Tools =
    [
        new("job_search", "agent-tool-job-search", [JobSearch], DespatchTile),
        new("job_story", "agent-tool-job-story", [JobSearch], DespatchTile),
        new("job_evidence", "agent-tool-job-evidence", [JobSearch], DespatchTile),
        new("recurring_check", "agent-tool-recurring-check", ["dw-recurring-jobs"], DespatchTile),
        new("partner_inbox", "agent-tool-partner-inbox", ["dw-partner-approvals"], DespatchTile),
        new("dispatch_board", "agent-tool-dispatch-board", ["dw-dispatch", "view-dispatch-board"], DespatchTile),
        new("driver_day", "agent-tool-driver-day", ["dw-driver-management"], DespatchTile)
    ];
}
