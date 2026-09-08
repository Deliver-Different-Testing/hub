namespace Hub.ViewModels;

/// <summary>
/// Where a courier's Integration Manager lives, as that deployment reports it about itself.
/// <para>
/// It is the only thing that knows its own public address, which is why registration is a call it
/// makes rather than a value configured by hand in two places.
/// </para>
/// <para>
/// Lifted out of <c>ShopifySignIn.cs</c>, which held the sign-in request and response types beside
/// it and went when the front door stopped asking Hub to check a merchant's credentials. This one
/// stayed: <c>PUT /api/tenants/{tenantId}/shopify-host</c> is still the only built way to switch a
/// courier on, even though nothing calls it yet.
/// </para>
/// </summary>
public sealed class ShopifyTenantHostRequest
{
    public string? IntegrationManagerUrl { get; init; }
}
