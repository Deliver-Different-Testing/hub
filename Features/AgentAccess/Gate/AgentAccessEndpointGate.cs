using Hub.Features.AgentAccess.Oauth;
using Serilog;

namespace Hub.Features.AgentAccess.Gate;

// Keeps the OAuth endpoints dark. Runs before authentication, so OpenIddict never sees a
// /connect/* or discovery request for a tenant whose agent access gate is closed. Claude's
// first calls carry no Hub cookie, so the tenant comes from the resolver, not the sign-in.
// While agent access is dark (not released, keys not persistent, any error, or no tenant)
// the answer is a plain 404, identical to a Hub that has no OAuth server at all. While the
// tenant itself has it switched off, discovery still answers so the AI app opens a browser,
// and authorize sends the person to a page that says why. Any other request passes straight
// through without touching the gate.
public sealed class AgentAccessEndpointGate(RequestDelegate next)
{
    public const string UnavailablePath = "/agent-access/unavailable";

    private const string ConnectPrefix = "/connect";
    private const string AuthorizePath = "/connect/authorize";
    private const string TokenPath = "/connect/token";
    private const string ConfigurationPath = "/.well-known/openid-configuration";
    private const string AuthorizationServerPath = "/.well-known/oauth-authorization-server";
    private const string JwksPath = "/.well-known/jwks";

    public async Task InvokeAsync(
        HttpContext context, IAgentAccessTenantResolver tenants, IAgentAccessGate gate, IAgentAccessClientSeeder seeder)
    {
        if (!IsAgentAccessPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        AgentAccessTenant? tenant = null;
        var decision = new AgentAccessDecision(false, AgentAccessReason.Error);
        try
        {
            tenant = await tenants.ResolveAsync(context.RequestAborted);
            if (tenant is not null)
            {
                decision = await gate.EvaluateAsync(context.RequestAborted);
            }
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

        if (tenant is null || !decision.Enabled)
        {
            if (tenant is not null && AgentAccessUnavailable.IsTenantOff(decision.Reason))
            {
                await AnswerTenantOffAsync(context, tenant);
                return;
            }

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

    // Switched off for this tenant: discovery answers (it holds no secrets) so the AI app gets as far as a browser,
    // authorize shows why, and the token endpoint refuses in OAuth's own terms. Nothing is seeded.
    private async Task AnswerTenantOffAsync(HttpContext context, AgentAccessTenant tenant)
    {
        var path = Trimmed(context.Request.Path);

        if (IsDiscoveryPath(path))
        {
            await next(context);
            return;
        }

        if (string.Equals(path, AuthorizePath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect(UnavailablePath);
            return;
        }

        if (string.Equals(path, TokenPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new Dictionary<string, string>
            {
                ["error"] = "invalid_grant",
                ["error_description"] = $"AI app access is switched off for {tenant.Name}."
            }, context.RequestAborted);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private static bool IsAgentAccessPath(PathString path) =>
        path.StartsWithSegments(ConnectPrefix, StringComparison.OrdinalIgnoreCase) || IsDiscoveryPath(Trimmed(path));

    private static bool IsDiscoveryPath(string? trimmed) =>
        string.Equals(trimmed, ConfigurationPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(trimmed, AuthorizationServerPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(trimmed, JwksPath, StringComparison.OrdinalIgnoreCase);

    private static string? Trimmed(PathString path) => path.Value?.TrimEnd('/');
}
