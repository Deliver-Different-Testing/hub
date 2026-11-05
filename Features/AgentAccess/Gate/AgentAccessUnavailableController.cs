using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Hub.Features.AgentAccess.Gate;

// Where the endpoint gate sends an authorize request while the tenant has agent access switched off: the person in the
// AI app's sign-in window is told why. It asks the gate itself rather than trusting a query string, and does not exist
// while agent access is dark.
[AllowAnonymous]
public class AgentAccessUnavailableController(IAgentAccessTenantResolver tenants, IAgentAccessGate gate) : Controller
{
    [HttpGet("~" + AgentAccessEndpointGate.UnavailablePath)]
    public async Task<IActionResult> Index()
    {
        string? message = null;
        try
        {
            if (await tenants.ResolveAsync(HttpContext.RequestAborted) is { } tenant)
            {
                var decision = await gate.EvaluateAsync(HttpContext.RequestAborted);
                message = AgentAccessUnavailable.Describe(decision.Reason, tenant.Name);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Agent access unavailable page could not evaluate the gate");
        }

        if (message is null) return NotFound();

        Response.Headers["X-Frame-Options"] = "DENY";
        Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
        Response.Headers.CacheControl = "no-store";
        return View("~/Features/AgentAccess/Views/Unavailable.cshtml", new AgentAccessUnavailableViewModel(message));
    }
}

public sealed record AgentAccessUnavailableViewModel(string Message);
