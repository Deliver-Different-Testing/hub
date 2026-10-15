using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess.Grants;

public enum AgentGrantEligibility
{
    Eligible,
    NoLongerEligible,
    Unknown,

    /// <summary>Agent access is switched off, forced off or the tenant is not live in its own database. The grant survives.</summary>
    TenantOff
}

/// <param name="CreatedUtc">When the user first approved this app; null when OpenIddict has no date, which is never trusted.</param>
public sealed record AgentGrantState(
    string? Status,
    DateTimeOffset? CreatedUtc,
    bool TenantOff,
    AgentGrantEligibility Eligibility);

public enum AgentGrantVerdict
{
    Allow,
    Refuse
}

public sealed record AgentGrantDecision(AgentGrantVerdict Verdict, bool RevokeGrant = false, string? Error = null, string? Description = null)
{
    public static AgentGrantDecision Allowed { get; } = new(AgentGrantVerdict.Allow);

    public static AgentGrantDecision Refused(string description, bool revoke = false) =>
        new(AgentGrantVerdict.Refuse, revoke, Errors.InvalidGrant, description);
}

// The rules a refresh has to pass (phase 1.6): the grant is still valid, is under twelve months old, the tenant has
// not been switched off, and the person behind it can still connect AI apps. Pure: no HTTP, Redis or database.
public static class AgentGrantPolicy
{
    private static readonly TimeSpan MaxGrantAge = TimeSpan.FromDays(365);

    public static AgentGrantDecision Decide(AgentGrantState state, DateTimeOffset now)
    {
        if (!string.Equals(state.Status, Statuses.Valid, StringComparison.Ordinal))
        {
            return AgentGrantDecision.Refused("The authorization is no longer valid.");
        }

        if (state.CreatedUtc is not { } created || created > now)
        {
            return AgentGrantDecision.Refused("The authorization has no trustworthy creation date.", revoke: true);
        }

        if (now - created >= MaxGrantAge)
        {
            return AgentGrantDecision.Refused("The authorization is older than twelve months; approve the app again.", revoke: true);
        }

        if (state.TenantOff)
        {
            return AgentGrantDecision.Refused("AI app access is switched off for this tenant.");
        }

        return state.Eligibility switch
        {
            AgentGrantEligibility.Eligible => AgentGrantDecision.Allowed,
            AgentGrantEligibility.NoLongerEligible => AgentGrantDecision.Refused("The user can no longer connect AI apps.", revoke: true),
            AgentGrantEligibility.TenantOff => AgentGrantDecision.Refused("AI app access is switched off for this tenant."),
            _ => AgentGrantDecision.Refused("The user could not be checked right now; try again shortly.")
        };
    }
}
