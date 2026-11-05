namespace Hub.Features.AgentAccess.Gate;

// What a person is told when agent access is off for this Hub's tenant. Only the reasons that belong to the tenant
// (its switch and the DF Admin force-off) are told; the rest (keys not persistent,
// an error) keep agent access dark, as if Hub had no OAuth server at all.
public static class AgentAccessUnavailable
{
    public static bool IsTenantOff(AgentAccessReason reason) =>
        reason is AgentAccessReason.TenantSwitchOff
            or AgentAccessReason.ForcedOff;

    /// <summary>The sentence for the unavailable page; null when there is nothing to tell (agent access is dark).</summary>
    public static string? Describe(AgentAccessReason reason, string tenantName) => reason switch
    {
        AgentAccessReason.TenantSwitchOff =>
            $"AI app access is switched off for {tenantName}. Ask your administrator to turn it on.",
        AgentAccessReason.ForcedOff =>
            $"AI app access has been switched off for {tenantName} by DFRNT. Contact DFRNT support if you need it.",
        AgentAccessReason.Enabled =>
            $"AI app access is on for {tenantName}. Close this window and connect the app again.",
        _ => null
    };
}
