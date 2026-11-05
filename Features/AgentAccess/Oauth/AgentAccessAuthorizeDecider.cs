using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess.Oauth;

/// <summary>The signed-in user, read from the Hub cookie. A null ClientTypeId or TenantId means the claim was absent.</summary>
public sealed record AgentAccessUser(string UserId, int? StaffId, int? ContactId, int? ClientTypeId, int? TenantId);

public sealed record AgentAccessAuthorizeInput(
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Resources,
    IReadOnlyList<string> Prompts,
    bool HasPermanentAuthorization);

public enum AgentAccessAuthorizeOutcome
{
    Reject,
    LoginRequired,
    TenantSwitchRequired,
    NeedsConsent,
    Issue
}

public sealed record AgentAccessAuthorizeDecision(AgentAccessAuthorizeOutcome Outcome, string? Error = null, string? Description = null)
{
    public static AgentAccessAuthorizeDecision Rejected(string error, string description) =>
        new(AgentAccessAuthorizeOutcome.Reject, error, description);
}

// The rules behind /connect/authorize (phase 1.5). Pure: no HTTP, no database.
public static class AgentAccessAuthorizeDecider
{
    public const string McpScope = "mcp";

    private static readonly HashSet<string> AllowedScopes = new(StringComparer.Ordinal) { McpScope, Scopes.OfflineAccess };
    private static readonly HashSet<int> AllowedClientTypes = [4, 5];

    public static AgentAccessAuthorizeDecision Decide(
        AgentAccessUser? user, AgentAccessAuthorizeInput input, string? mcpResource, int? hubTenantId)
    {
        if (mcpResource is null
            || input.Resources.Count != 1
            || !string.Equals(input.Resources[0], mcpResource, StringComparison.Ordinal))
        {
            return AgentAccessAuthorizeDecision.Rejected(
                Errors.InvalidTarget, "The resource is not this tenant's MCP server.");
        }

        if (!input.Scopes.Contains(McpScope) || input.Scopes.Any(s => !AllowedScopes.Contains(s)))
        {
            return AgentAccessAuthorizeDecision.Rejected(Errors.InvalidScope, "Only the mcp and offline_access scopes are available.");
        }

        var promptNone = input.Prompts.Contains(PromptValues.None);

        if (user is null)
        {
            return promptNone
                ? AgentAccessAuthorizeDecision.Rejected(Errors.LoginRequired, "The user is not signed in.")
                : new AgentAccessAuthorizeDecision(AgentAccessAuthorizeOutcome.LoginRequired);
        }

        if (string.IsNullOrWhiteSpace(user.UserId) || user.TenantId is null)
        {
            return AgentAccessAuthorizeDecision.Rejected(Errors.AccessDenied, "This user cannot connect AI apps.");
        }

        if (hubTenantId is null)
        {
            return AgentAccessAuthorizeDecision.Rejected(Errors.AccessDenied, "This Hub cannot tell which tenant it serves.");
        }

        // A Hub connects AI apps to its own tenant only. Someone signed in to another (DFRNT staff can belong to several)
        // is offered a switch first; the cookie's client type belongs to that other tenant, so it is judged afterwards.
        if (user.TenantId != hubTenantId)
        {
            return promptNone
                ? AgentAccessAuthorizeDecision.Rejected(Errors.InteractionRequired, "The user is signed in to a different tenant.")
                : new AgentAccessAuthorizeDecision(AgentAccessAuthorizeOutcome.TenantSwitchRequired);
        }

        if (user.ClientTypeId is not { } clientType || !AllowedClientTypes.Contains(clientType))
        {
            return AgentAccessAuthorizeDecision.Rejected(Errors.AccessDenied, "This user cannot connect AI apps.");
        }

        if (input.HasPermanentAuthorization && !input.Prompts.Contains(PromptValues.Consent))
        {
            return new AgentAccessAuthorizeDecision(AgentAccessAuthorizeOutcome.Issue);
        }

        return promptNone
            ? AgentAccessAuthorizeDecision.Rejected(Errors.ConsentRequired, "The user has not approved this app.")
            : new AgentAccessAuthorizeDecision(AgentAccessAuthorizeOutcome.NeedsConsent);
    }
}
