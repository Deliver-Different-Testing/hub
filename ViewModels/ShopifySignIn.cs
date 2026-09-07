namespace Hub.ViewModels;

/// <summary>
/// A merchant's dispatch credentials, as the Shopify front door relays them.
/// <para>
/// The shop is not here. It travels in the query string, because the rate limiter partitions on it
/// and a partitioner only ever sees the <c>HttpContext</c> - never a deserialised body. One place
/// for it means the two cannot disagree.
/// </para>
/// </summary>
public sealed class ShopifySignInRequest
{
    /// <summary>Matched against master <c>dbo.[User].Email</c>, and afterwards, inside the courier's
    /// own database, against <c>tucClientContact.UserName</c>.</summary>
    public string? Username { get; init; }

    public string? Password { get; init; }
}

/// <summary>
/// The second call, made only when the first offered a choice. Carries the authentication forward
/// so the merchant types their password once.
/// </summary>
public sealed class ShopifySelectTenantRequest
{
    public string? SelectionTicket { get; init; }

    public int TenantId { get; init; }
}

/// <summary>
/// What Hub tells the front door about a sign-in.
/// <para>
/// Everything absent from this type is absent on purpose. No password, salt, hash flag or reset key;
/// no <c>Tenant.DBConnection</c>; no <c>CurrentTenantId</c>; no user id, which belongs in the signed
/// ticket and not in a body the browser will see. And no tenant the merchant belongs to that is not
/// Shopify-enabled - listing those would hand the public front door a map of the merchant's other
/// courier relationships.
/// </para>
/// </summary>
public sealed class ShopifySignInResponse
{
    public required string Email { get; init; }

    /// <summary>Every courier the merchant may connect this store to. Empty is a valid answer.</summary>
    public required IReadOnlyList<ShopifySignInTenant> Tenants { get; init; }

    /// <summary>Set only when exactly one courier is in play, either straight away or after a choice.</summary>
    public ShopifySignInTenant? Tenant { get; init; }

    /// <summary>Set with <see cref="Tenant"/>. What the tenant's Integration Manager validates.</summary>
    public string? LinkTicket { get; init; }

    public DateTime? LinkTicketExpiresAtUtc { get; init; }

    /// <summary>Set instead, when there is a choice to make.</summary>
    public string? SelectionTicket { get; init; }
}

/// <summary>
/// Where a courier's Integration Manager lives, as that deployment reports it about itself.
/// <para>
/// It is the only thing that knows its own public address, which is why registration is a call it
/// makes rather than a value configured by hand in two places.
/// </para>
/// </summary>
public sealed class ShopifyTenantHostRequest
{
    public string? IntegrationManagerUrl { get; init; }
}
