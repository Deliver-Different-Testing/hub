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
        ("driver_day", "agent-tool-driver-day", "", "hub-tile-despatchweb"),
        ("automation_engine_pulse", "agent-tool-automation-pulse", "cfg-automations", "hub-tile-configurator"),
        ("automation_firing_census", "agent-tool-automation-census", "cfg-automations", "hub-tile-configurator"),
        ("automation_job_trail", "agent-tool-automation-job-trail", "cfg-automations", "hub-tile-configurator"),
        ("rule_readback", "agent-tool-rule-readback", "cfg-automations", "hub-tile-configurator"),
        ("client_month_report", "agent-tool-client-month-report", "reports", "hub-tile-configurator"),
        ("courier_day_report", "agent-tool-courier-day-report", "reports", "hub-tile-configurator"),
        ("list_entities", "agent-tool-list-entities", "reports", "hub-tile-configurator"),
        ("describe_entity", "agent-tool-describe-entity", "reports", "hub-tile-configurator"),
        ("run_report", "agent-tool-run-report", "reports", "hub-tile-configurator"),
        ("fleet_compliance", "agent-tool-fleet-compliance", "cfg-fleet,courier-compliance-monitoring", "hub-tile-configurator"),
        ("compliance_chase_history", "agent-tool-compliance-chase-history", "cfg-fleet,courier-compliance-monitoring", "hub-tile-configurator"),
        ("agent_onboarding_status", "agent-tool-agent-onboarding-status", "multi-client,courier-compliance-monitoring", "hub-tile-configurator"),
        ("access_explain", "agent-tool-access-explain", "cfg-settings-tile-access", "hub-tile-configurator"),
        ("rate_change_history", "agent-tool-rate-change-history", "cfg-pricing", "hub-tile-configurator")
    ];

    [Fact]
    public void The_tool_rows_match_the_shared_catalogue()
    {
        Assert.Equal(
            Expected,
            AgentToolCatalogue.Tools.Select(t => (t.Tool, t.Verb, string.Join(",", t.ScreenKeys), t.HubTile)).ToArray());
    }

    [Fact]
    public void All_lists_every_tool_verb_and_the_other_verbs()
    {
        string[] expected =
        [
            "agent-connect", "agent-export-jobs", "agent-export-pricing", "agent-see-costs",
            "agent-tool-job-search", "agent-tool-job-story", "agent-tool-job-evidence", "agent-tool-recurring-check",
            "agent-tool-partner-inbox", "agent-tool-dispatch-board", "agent-tool-driver-day",
            "agent-tool-automation-pulse", "agent-tool-automation-census", "agent-tool-automation-job-trail", "agent-tool-rule-readback", "agent-tool-client-month-report", "agent-tool-courier-day-report", "agent-tool-list-entities", "agent-tool-describe-entity", "agent-tool-run-report", "agent-tool-fleet-compliance", "agent-tool-compliance-chase-history", "agent-tool-agent-onboarding-status", "agent-tool-access-explain", "agent-tool-rate-change-history",
            "agent-see-pod", "agent-see-contact-details", "agent-see-notes", "agent-manage-connections"
        ];

        Assert.Equal(expected.Order(), AgentCapabilities.All.Order());
    }
}
