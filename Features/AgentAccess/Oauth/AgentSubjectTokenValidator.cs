using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Hub.Features.AgentAccess;

public sealed record AgentSubjectToken(string AuthorizationId, string Subject, int TenantId);

public sealed record AgentSubjectTokenResult(AgentSubjectToken? Token, string? Error)
{
    public static AgentSubjectTokenResult Fail(string error) => new(null, error);
}

public interface IAgentSubjectTokenValidator
{
    Task<AgentSubjectTokenResult> ValidateAsync(string token);
}

// OpenIddict validates the subject token too, but on its own it accepts an app pass or a token from another tenant's
// Hub (the signing key is shared), so these checks are required. A refactor that tried dropping this class failed the
// wrong-audience and other-issuer exchange tests.
// Validates the agent pass the MCP server presents for exchange (phase 1.9): our signature, this Hub as the issuer,
// this tenant's MCP server as the audience, still within its lifetime. Nothing here trusts the token's own claims
// until the signature has been checked against the keys this Hub signs with.
public sealed class AgentSubjectTokenValidator(
    IOptionsMonitor<OpenIddictServerOptions> serverOptions,
    AgentAccessMcpResource resource,
    TimeProvider time) : IAgentSubjectTokenValidator
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public async Task<AgentSubjectTokenResult> ValidateAsync(string token)
    {
        var options = serverOptions.CurrentValue;
        if (resource.Value is null || options.Issuer is null) return AgentSubjectTokenResult.Fail("The server is not configured for token exchange.");
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) return AgentSubjectTokenResult.Fail("The subject token is missing or too large.");

        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = options.SigningCredentials.Select(c => c.Key).ToList(),
            ValidateIssuer = true,
            ValidIssuer = options.Issuer.AbsoluteUri,
            ValidateAudience = true,
            ValidAudience = resource.Value,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = ClockSkew,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidTypes = ["at+jwt"],
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = time.GetUtcNow().UtcDateTime;
                return expires is { } end && end + ClockSkew > now && (notBefore is not { } start || start - ClockSkew <= now);
            },
            NameClaimType = "name",
            RoleClaimType = "role"
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
        if (!result.IsValid) return AgentSubjectTokenResult.Fail("The subject token is not valid.");

        var claims = result.ClaimsIdentity.Claims;
        string? Claim(string type) => claims.FirstOrDefault(c => c.Type == type)?.Value;

        if (Claim("oi_au_id") is not { Length: > 0 } authorizationId
            || Claim("sub") is not { Length: > 0 } subject
            || !int.TryParse(Claim("tenant_id"), out var tenantId))
        {
            return AgentSubjectTokenResult.Fail("The subject token is not an agent pass.");
        }

        return new AgentSubjectTokenResult(new AgentSubjectToken(authorizationId, subject, tenantId), null);
    }
}
