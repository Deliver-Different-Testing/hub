using Hub.Controllers.Api;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Hub.Features.AgentAccess.Gate;

// The gate sends an authorize request to the SPA's /agent-access/unavailable page while the tenant has agent access
// switched off; the page reads why from here, so the person in the AI app's sign-in window is told. It asks the gate
// itself rather than trusting a query string, and does not exist while agent access is dark.
[Route("api/agent-access/unavailable")]
[AllowAnonymous]
public class AgentAccessUnavailableController(IAgentAccessTenantResolver tenants, IAgentAccessGate gate) : SpaApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        Response.Headers.CacheControl = "no-store";

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

        return message is null
            ? NotFound(new ApiError("Not found."))
            : Ok(new AgentAccessUnavailableResponse(message));
    }
}

public sealed record AgentAccessUnavailableResponse(string Message);
