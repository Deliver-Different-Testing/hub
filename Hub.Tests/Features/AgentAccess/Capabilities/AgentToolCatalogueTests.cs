using Hub.Features.AgentAccess.Capabilities;

namespace Hub.Tests.Features.AgentAccess.Capabilities;

// Hub mirrors DeliverDifferent.AgentAccess.AgentToolCatalogue (it cannot reference that package). This pins every row
// of the package's table, so a change on either side that is not made on both fails the build.
public class AgentToolCatalogueTests
{
    private static readonly (string Tool, string Verb, string Screens, string Tile)[] Expected =
    [
        ("job_search", "agent-tool-job-search", "", "hub-tile-despatchweb"),
        ("job_story", "agent-tool-job-story", "", "hub-tile-despatchweb"),
        ("job_evidence", "agent-tool-job-evidence", "", "hub-tile-despatchweb"),
        ("recurring_check", "agent-tool-recurring-check", "", "hub-tile-despatchweb"),
        ("partner_inbox", "agent-tool-partner-inbox", "", "hub-tile-despatchweb"),
        ("dispatch_board", "agent-tool-dispatch-board", "view-dispatch-board", "hub-tile-despatchweb"),
        ("driver_day", "agent-tool-driver-day", "", "hub-tile-despatchweb")
    ];

    [Fact]
    public void The_tool_rows_match_the_shared_catalogue()
    {
        Assert.Equal(
            Expected,
            AgentToolCatalogue.Tools.Select(t => (t.Tool, t.Verb, string.Join(",", t.ScreenKeys), t.HubTile)).ToArray());
    }

    [Fact]
    public void All_lists_the_eleven_new_keys_and_the_original_four()
    {
        string[] expected =
        [
            "agent-connect", "agent-export-jobs", "agent-export-pricing", "agent-see-costs",
            "agent-tool-job-search", "agent-tool-job-story", "agent-tool-job-evidence", "agent-tool-recurring-check",
            "agent-tool-partner-inbox", "agent-tool-dispatch-board", "agent-tool-driver-day",
            "agent-see-pod", "agent-see-contact-details", "agent-see-notes", "agent-manage-connections"
        ];

        Assert.Equal(expected.Order(), AgentCapabilities.All.Order());
    }
}
