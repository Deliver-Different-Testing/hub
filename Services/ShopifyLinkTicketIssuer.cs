using System.Security.Claims;
using System.Security.Cryptography;
using Hub.Interfaces;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace Hub.Services;

/// <summary>
/// The ES256 key Hub signs Shopify tickets with, loaded once and held.
/// <para>
/// One instance, not a key created per signature: IdentityModel caches a signature provider against
/// the <see cref="SecurityKey"/> it was handed, so a key disposed after use comes back out of that
/// cache on the next call and throws. Rotation is a redeploy with a new value, which is how every
/// other secret in this service is rotated.
/// </para>
/// </summary>
public sealed class ShopifyLinkTicketKey : IDisposable
{
    private readonly ECDsa _ecdsa;

    public ShopifyLinkTicketKey(string? privateKeyPem, string? keyId)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            // Loud rather than a ticket nobody can verify: without a key there is no statement a
            // tenant will believe, and a sign-in that appears to succeed and then fails at hand-over
            // is the worst of both.
            throw new InvalidOperationException(
                $"{ShopifyLinkTicket.PrivateKeyVariable} is not configured; Shopify merchant sign-in cannot issue tickets.");
        }

        _ecdsa = ECDsa.Create();

        try
        {
            _ecdsa.ImportFromPem(privateKeyPem);
        }
        catch
        {
            _ecdsa.Dispose();
            throw new InvalidOperationException(
                $"{ShopifyLinkTicket.PrivateKeyVariable} is not a readable PEM-encoded EC private key.");
        }

        // The kid travels in the JWT header so a tenant holding several public keys knows which to
        // try, which is what makes rotation "publish the new public key everywhere, then switch
        // Hub's private key" rather than a flag day.
        SecurityKey = new ECDsaSecurityKey(_ecdsa) { KeyId = keyId };
    }

    /// <summary>Reads the key out of the environment, the way Hub reads its other secrets.</summary>
    public static ShopifyLinkTicketKey FromEnvironment() => new(
        Environment.GetEnvironmentVariable(ShopifyLinkTicket.PrivateKeyVariable),
        Environment.GetEnvironmentVariable(ShopifyLinkTicket.KeyIdVariable));

    public ECDsaSecurityKey SecurityKey { get; }

    public void Dispose() => _ecdsa.Dispose();
}

/// <summary>
/// Signs Shopify link and selection tickets with ES256.
/// <para>
/// Not <c>JWTSecretKey</c>, for two reasons. That key is symmetric and already present in every
/// tenant's Integration Manager, so any tenant holding it could mint a ticket asserting any user for
/// any <em>other</em> tenant - strictly worse than the shared hand-over key this is meant to improve
/// on. And Integration Manager's platform bearer scheme validates that key with
/// <c>ValidateLifetime = false</c>, which would make a two-minute ticket eternal.
/// </para>
/// <para>
/// A public key is not a secret. It ships in every tenant's configuration, can be committed, and can
/// be distributed weeks before anything uses it - which is what makes rotation two ordinary deploys
/// rather than a flag day.
/// </para>
/// </summary>
public sealed class ShopifyLinkTicketIssuer(ShopifyLinkTicketKey key, TimeProvider timeProvider)
    : IShopifyLinkTicketIssuer
{
    private static readonly JsonWebTokenHandler Handler = new();

    public ShopifyLinkTicketIssuer(ShopifyLinkTicketKey key) : this(key, TimeProvider.System)
    {
    }

    public ShopifyTicket IssueLink(int userId, string email, int tenantId, string shop) =>
        Issue(ShopifyLinkTicket.LinkAudience, userId, email, shop,
        [
            new Claim(ShopifyLinkTicket.TenantClaim, tenantId.ToString())
        ]);

    public ShopifyTicket IssueSelection(int userId, string email, string shop,
        IReadOnlyCollection<int> candidateTenantIds) =>
        Issue(ShopifyLinkTicket.SelectAudience, userId, email, shop,
            [.. candidateTenantIds.Select(id => new Claim(ShopifyLinkTicket.CandidateClaim, id.ToString()))]);

    public ShopifySelection? ReadSelection(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        // Validated with our own key rather than trusted because we minted it. The candidate list is
        // not the authority - membership is re-read from master before a link ticket is issued - but
        // an unsigned one would make the second call a way to name any tenant at all.
        var result = Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = key.SecurityKey,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidIssuer = ShopifyLinkTicket.Issuer,
            // A link ticket presented here must fail: it is already bound to one tenant, and
            // exchanging it would be a way to have it re-issued naming another.
            ValidAudience = ShopifyLinkTicket.SelectAudience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                return (notBefore is null || notBefore <= now) && expires > now;
            }
        }).GetAwaiter().GetResult();

        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            Log.Debug("Shopify selection ticket rejected: {Reason}", result.Exception?.Message ?? "invalid");
            return null;
        }

        if (!int.TryParse(jwt.Subject, out var userId))
        {
            return null;
        }

        var tenantIds = jwt.Claims
            .Where(c => c.Type == ShopifyLinkTicket.CandidateClaim)
            .Select(c => int.TryParse(c.Value, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

        return new ShopifySelection(
            userId,
            jwt.GetClaim(ShopifyLinkTicket.EmailClaim).Value,
            jwt.GetClaim(ShopifyLinkTicket.ShopClaim).Value,
            tenantIds);
    }

    private ShopifyTicket Issue(string audience, int userId, string email, string shop, Claim[] extra)
    {
        var issuedAt = timeProvider.GetUtcNow().UtcDateTime;
        var expires = issuedAt.Add(ShopifyLinkTicket.Lifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ShopifyLinkTicket.EmailClaim, email),
            // Never an identity on its own - the receiving side checks it against the shop in the
            // request it arrived with, so a ticket for one store cannot provision another.
            new(ShopifyLinkTicket.ShopClaim, shop),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(extra);

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ShopifyLinkTicket.Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expires,
            SigningCredentials = new SigningCredentials(key.SecurityKey, SecurityAlgorithms.EcdsaSha256)
        });

        return new ShopifyTicket(token, expires);
    }
}
