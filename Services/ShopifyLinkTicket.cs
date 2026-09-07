namespace Hub.Services;

/// <summary>
/// The wire contract for the Shopify link ticket, in one place because it has readers in another
/// solution: every tenant's Integration Manager validates these, and nothing compiles against both.
/// Change a value here and the same value has to change there.
/// </summary>
public static class ShopifyLinkTicket
{
    public const string Issuer = "hub";

    /// <summary>
    /// A ticket bound to one tenant. Presented to that tenant's Integration Manager, which refuses
    /// it unless <see cref="TenantClaim"/> is its own deployment's tenant.
    /// </summary>
    public const string LinkAudience = "shopify-link";

    /// <summary>
    /// Authentication carried forward while a merchant who belongs to several couriers chooses one.
    /// Names no tenant, and is only ever read back by Hub - so a half-finished sign-in cannot
    /// provision anything.
    /// </summary>
    public const string SelectAudience = "shopify-select";

    public const string EmailClaim = "email";
    public const string TenantClaim = "tid";
    public const string ShopClaim = "shop";

    /// <summary>Candidate tenants on a selection ticket. Repeated, one claim per tenant.</summary>
    public const string CandidateClaim = "cand";

    /// <summary>
    /// Two minutes. The ticket is consumed by the very next thing the front door does - a token
    /// exchange, then the provision call - so this covers a slow exchange and clock skew and nothing
    /// else. It is a bearer credential; it should not loiter.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(120);

    /// <summary>The private key, PKCS#8 PEM. Hub only - no tenant ever holds this.</summary>
    public const string PrivateKeyVariable = "ShopifyLinkTicketPrivateKey";

    /// <summary>
    /// Names the key in the JWT header, so a tenant holding several public keys knows which to try.
    /// Rotation is: publish the new public key everywhere, then switch this and the private key.
    /// </summary>
    public const string KeyIdVariable = "ShopifyLinkTicketKeyId";
}

/// <summary>A signed ticket and the moment it stops being one.</summary>
public sealed record ShopifyTicket(string Token, DateTime ExpiresAtUtc);

/// <summary>What a selection ticket says, once Hub has verified its own signature on it.</summary>
public sealed record ShopifySelection(int UserId, string Email, string Shop, IReadOnlyList<int> TenantIds);
