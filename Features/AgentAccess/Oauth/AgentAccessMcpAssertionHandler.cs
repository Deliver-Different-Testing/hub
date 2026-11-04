using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Serilog;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess.Oauth;

// Lets the MCP server authenticate as dfrnt-mcp with its Kubernetes projected service-account token. OpenIddict only
// trusts client assertions signed with the client's own registered keys and shaped as RFC 7523 client assertions
// (iss = sub = client_id), which a token minted by the cluster can never be. So for dfrnt-mcp alone, and only on the
// token endpoint, this handler validates the assertion against the cluster's keys before the built-in validation runs,
// then stands in for it: the built-in check is switched off for this one request and the client is recorded as
// authenticated by assertion. Any other client, or dfrnt-mcp with a secret, goes through OpenIddict untouched.
public sealed class AgentAccessMcpAssertionHandler(IAgentAccessMcpAssertionValidator assertions)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessAuthenticationContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ProcessAuthenticationContext>()
            .UseSingletonHandler<AgentAccessMcpAssertionHandler>()
            // After ResolveValidatedTokens and ValidateRequiredTokens have read client_assertion off the request,
            // before ValidateClientAssertion would check it against dfrnt-mcp's own (non-existent) key set.
            .SetOrder(OpenIddictServerHandlers.ValidateClientAssertion.Descriptor.Order - 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessAuthenticationContext context)
    {
        if (context.EndpointType != OpenIddictServerEndpointType.Token
            || !assertions.Enabled
            || !string.Equals(context.ClientId, AgentAccessClientSeeder.ReservedTokenExchangeClientId, StringComparison.Ordinal)
            || string.IsNullOrEmpty(context.ClientAssertion)
            || !string.Equals(context.ClientAssertionType, ClientAssertionTypes.JwtBearer, StringComparison.Ordinal))
        {
            return;
        }

        var issuer = context.Options.Issuer ?? context.BaseUri
                     ?? throw new InvalidOperationException("The token endpoint has no issuer to check the assertion audience against.");
        var audience = issuer.AbsoluteUri;
        var result = await assertions.ValidateAsync(context.ClientAssertion, [audience, audience.TrimEnd('/')]);
        if (!result.Accepted)
        {
            Log.Warning("Agent access refused the MCP server's service-account token: {Reason}", result.Failure);
            context.Reject(Errors.InvalidClient, "The client assertion is not valid.");
            return;
        }

        context.ValidateClientAssertion = false;
        context.ClientAssertionPrincipal = AuthenticatedClient(audience, result.ExpiresAt!.Value);
    }

    // What the built-in validation would have produced for a valid RFC 7523 assertion from dfrnt-mcp, so the
    // well-known-claims, issuer and client-type checks that follow see an authenticated confidential client.
    private static ClaimsPrincipal AuthenticatedClient(string audience, DateTimeOffset expiresAt)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(nameof(AgentAccessMcpAssertionHandler)));
        principal.SetClaim(Claims.Issuer, AgentAccessClientSeeder.ReservedTokenExchangeClientId)
            .SetClaim(Claims.Subject, AgentAccessClientSeeder.ReservedTokenExchangeClientId)
            .SetAudiences(audience)
            .SetExpirationDate(expiresAt)
            .SetTokenType(TokenTypeIdentifiers.Private.ClientAssertion);
        return principal;
    }
}
