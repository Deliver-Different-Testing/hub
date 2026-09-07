using Hub.Services;

namespace Hub.Interfaces;

/// <summary>
/// Mints the tickets that carry a Shopify merchant sign-in: one that names the tenant a store is
/// being connected to, and one that carries the authentication while a merchant with more than one
/// courier chooses between them.
/// </summary>
public interface IShopifyLinkTicketIssuer
{
    /// <summary>
    /// A ticket for one tenant and one store, presented to that tenant's Integration Manager.
    /// The caller must have confirmed the user's membership of <paramref name="tenantId"/> against
    /// master first: this signs what it is told.
    /// </summary>
    ShopifyTicket IssueLink(int userId, string email, int tenantId, string shop);

    /// <summary>
    /// A ticket that says only "this person authenticated, for this store, and these were their
    /// options". Read back by <see cref="ReadSelection"/> on the second call.
    /// </summary>
    ShopifyTicket IssueSelection(int userId, string email, string shop, IReadOnlyCollection<int> candidateTenantIds);

    /// <summary>
    /// What a selection ticket says, or null if it is expired, tampered with, not a ticket, or a
    /// link ticket being passed off as one.
    /// </summary>
    ShopifySelection? ReadSelection(string? token);
}
