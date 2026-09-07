namespace Hub.ViewModels;

/// <summary>
/// Which tenant owns a Shopify shop.
/// <para>
/// Both identifiers come back together on purpose. Integration Manager needs the id to stamp the
/// <c>CurrentTenantID</c> claim and to fetch the connection string, and the code because
/// Shopify.Core keys tenant metadata - the wall-clock timezone its booking rules evaluate against -
/// by code. Returning one would cost a second round trip on the checkout path.
/// </para>
/// </summary>
public sealed record ShopifyShopTenantResponse
{
    public required string Shop { get; init; }

    /// <summary>
    /// The store's display name, where the front door has been able to read one. A label for the
    /// people reading this, never something to key off - it is nullable, and it can be stale.
    /// </summary>
    public string? ShopName { get; init; }

    public int TenantId { get; init; }

    /// <summary>The tenant's <c>Tenant.Code</c>. Null when the tenant row has none recorded.</summary>
    public string? TenantCode { get; init; }
}
