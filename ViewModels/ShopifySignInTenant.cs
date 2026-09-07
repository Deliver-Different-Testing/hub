namespace Hub.ViewModels;

/// <summary>
/// One courier a signed-in merchant may connect their store to.
/// <para>
/// Deliberately four fields. This travels to the Shopify front door, which is the public App URL
/// and the most exposed service in the estate, so it carries what routing needs and nothing that
/// describes the tenant further - and above all never <c>Tenant.DBConnection</c>.
/// </para>
/// </summary>
public sealed class ShopifySignInTenant
{
    public required int TenantId { get; init; }

    public string? Code { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// The courier's own Integration Manager, from <c>dbo.ShopifyTenantHost</c>. Its presence is
    /// what made this tenant a candidate at all.
    /// </summary>
    public required string Host { get; init; }
}
