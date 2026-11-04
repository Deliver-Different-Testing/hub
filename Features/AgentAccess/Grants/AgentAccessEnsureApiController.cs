using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Keys;
using Hub.Features.AgentAccess.Oauth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hub.Features.AgentAccess.Grants;

public sealed record AgentAccessEnsureCheck(string Key, bool Ok, string Detail);

public sealed record AgentAccessEnsureResult(bool Ready, string Reason, IReadOnlyList<AgentAccessEnsureCheck> Checks);

// Integration Manager calls this after an admin saves the AI app access switch. Hub sets itself up (seeds the OAuth
// clients, but only while the gate is open) and reports whether agent access is ready. Safe to repeat or run
// concurrently: the seeder is idempotent and nothing else is written.
[AllowAnonymous]
[EnableRateLimiting("api")]
public class AgentAccessEnsureApiController(
    IAgentAccessTenantResolver tenants,
    IAgentAccessGate gate,
    IAgentAccessClientSeeder seeder,
    IAgentAccessKeyState keys,
    AgentAccessIssuer issuer) : Controller
{
    [HttpPost("~/api/agent-access/tenants/{tenantId:int}/ensure")]
    public async Task<IActionResult> Ensure(
        int tenantId,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken cancellationToken)
    {
        if (!AgentAccessApiKey.IsValid(apiKey)) return Unauthorized();
        if (tenantId <= 0) return BadRequest();

        var tenant = await tenants.ResolveAsync(cancellationToken);
        if (tenant is null || tenant.TenantId != tenantId) return NotFound();

        var decision = await gate.EvaluateAsync(cancellationToken);
        if (decision.Enabled)
        {
            await seeder.EnsureAsync(cancellationToken);
        }

        var clientsSeeded = decision.Enabled && seeder.Seeded;
        var checks = new List<AgentAccessEnsureCheck>
        {
            keys.Persistent
                ? new("keys", true, "Sign-in keys are stored safely and shared across servers.")
                : new("keys", false, "Sign-in keys are not set up on this server yet."),
            issuer.Value is not null
                ? new("issuer", true, "The sign-in address is set.")
                : new("issuer", false, "The sign-in address is missing or not valid."),
            SwitchCheck(decision.Reason),
            clientsSeeded
                ? new("clients", true, "The AI apps are registered.")
                : new("clients", false, decision.Enabled ? "The AI apps could not be registered yet." : "The AI apps are not registered until access is on."),
            seeder.TokenExchangeConfigured
                ? new("mcpExchange", true, "The connection to the DFRNT MCP server is set up.")
                : new("mcpExchange", false, "The connection to the DFRNT MCP server is not set up.")
        };

        return Ok(new AgentAccessEnsureResult(decision.Enabled && checks.All(c => c.Ok), decision.Reason.ToString(), checks));
    }

    private static AgentAccessEnsureCheck SwitchCheck(AgentAccessReason reason) => reason switch
    {
        AgentAccessReason.ForcedOff => new("switch", false, "AI app access has been turned off for this account by DFRNT."),
        AgentAccessReason.TenantSwitchOff => new("switch", false, "AI app access is switched off."),
        AgentAccessReason.Error => new("switch", false, "The AI app access setting could not be read."),
        // The gate reads the switch only after the keys, so it says nothing about the switch here.
        AgentAccessReason.KeysNotPersistent => new("switch", false, "The AI app access setting is not checked until the sign-in keys are set up."),
        _ => new("switch", true, "AI app access is switched on.")
    };
}
