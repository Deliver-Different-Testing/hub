namespace Hub.ViewModels;

/// <summary>
/// What recording a shop-to-tenant pairing did. An enum rather than a bool because the caller has
/// to tell "already recorded, nothing to do" from "this shop belongs to someone else", and the two
/// have opposite consequences.
/// </summary>
public enum ShopifyShopMappingResult
{
    /// <summary>The pairing did not exist and was inserted.</summary>
    Mapped,

    /// <summary>This exact pairing already existed. A reinstall, which is a normal thing to happen.</summary>
    AlreadyMapped,

    /// <summary>
    /// The shop is already recorded against a different tenant. Never resolved automatically:
    /// re-pointing it would move a live merchant's orders into another courier's database.
    /// </summary>
    ConflictsWithAnotherTenant,

    /// <summary>No tenant with that id.</summary>
    TenantNotFound
}
