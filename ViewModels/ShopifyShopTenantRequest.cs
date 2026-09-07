namespace Hub.ViewModels;

/// <summary>The shop being paired with a tenant, sent when a merchant completes the Shopify install.</summary>
public sealed record ShopifyShopTenantRequest
{
    public required string Shop { get; init; }
}
