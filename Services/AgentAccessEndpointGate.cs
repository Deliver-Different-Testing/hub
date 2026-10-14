using Hub.Interfaces;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace Hub.Services;

// Keeps the OAuth endpoints dark. Runs before authentication, so OpenIddict never sees a
// /connect/* or discovery request for a tenant whose agent access gate is closed: the
// answer is a plain 404, identical to a Hub that has no OAuth server at all. Any other
// request passes straight through without touching the gate.
public sealed class AgentAccessEndpointGate(RequestDelegate next)
{
    private const string ConnectPrefix = "/connect";
    private const string ConfigurationPath = "/.well-known/openid-configuration";
    private const string JwksPath = "/.well-known/jwks";

    public async Task InvokeAsync(HttpContext context, IAgentAccessGate gate, IAgentAccessClientSeeder seeder)
    {
        if (!IsAgentAccessPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        var open = false;
        try
        {
            open = (await gate.EvaluateAsync(context.RequestAborted)).Enabled;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail closed: if the gate cannot answer, the endpoints do not exist.
            Log.Error(ex, "Agent access gate failed for {Path}; answering 404", context.Request.Path);
        }

        if (!open)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // The pre-registered OAuth clients are written to the Master DB only once agent access is open
        // for a tenant: until then Hub never writes to the OAuth store. The seeder never throws, and a
        // failure must not take an open endpoint down.
        try
        {
            await seeder.EnsureAsync(context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Agent access client seeding failed for {Path}; continuing", context.Request.Path);
        }

        await next(context);
    }

    private static bool IsAgentAccessPath(PathString path)
    {
        if (path.StartsWithSegments(ConnectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trimmed = path.Value?.TrimEnd('/');
        return string.Equals(trimmed, ConfigurationPath, StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, JwksPath, StringComparison.OrdinalIgnoreCase);
    }
}
