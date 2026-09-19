namespace Hub.Interfaces;

// Gate 2 of the tile-level access model (Steve, 2026-08-26).
//
//   Gate 1  DF Admin enables features for the tenant  -> ClientTypeFeature, IFeatureResolver
//   Gate 2  Tenant admin grants roles access to tiles -> RolePermission on hub-tile-*, THIS
//
// A tile is visible only if BOTH gates pass. There is deliberately no
// action-level permission model within a tile: if a role can see a tile it can
// use and edit everything under it.
//
// WHY THE RESULT CARRIES A REASON. Effective visibility is an AND of several
// independent conditions, and "the tile is missing" is the same symptom for all
// of them. Support cannot debug that, so every decision reports WHICH gate
// closed. Do not reduce this to a HashSet<string> - the set is derivable from
// the decisions, but the reasons are not recoverable from the set.
public interface ITileAccessResolver
{
    /// <summary>
    /// Decides each hub tile for one contact.
    /// </summary>
    /// <param name="contactId">tucClientContact.ucctID from the ContactID claim.</param>
    /// <param name="clientId">
    /// The contact's client. Used for per-client grant overrides, and to derive the
    /// ClientType for the DF Admin bypass - derived here rather than passed in so
    /// callers do not have to duplicate the lookup IFeatureResolver already does.
    /// </param>
    /// <param name="featureEnabledKeys">
    /// The gate-1 result from <see cref="IFeatureResolver"/>. Tiles absent from this
    /// set are not enabled for the tenant and are reported as such.
    /// </param>
    Task<IReadOnlyList<TileAccess>> ResolveAsync(
        int contactId, int? clientId, ISet<string> featureEnabledKeys);
}

/// <summary>One tile's decision, and why.</summary>
/// <param name="TileKey">The `hub-tile-*` feature key.</param>
/// <param name="Granted">Whether the tile should render.</param>
/// <param name="Reason">Which gate decided it.</param>
public sealed record TileAccess(string TileKey, bool Granted, TileAccessReason Reason);

public enum TileAccessReason
{
    /// <summary>Both gates passed: enabled for the tenant and granted to a role the contact holds.</summary>
    Granted,

    /// <summary>DF Admin (ClientType 5) bypasses gate 2, as it does gate 1.</summary>
    DfAdminBypass,

    /// <summary>
    /// The contact holds no role carrying any tile grant, so gate 2 is not yet
    /// configured for them and does not restrict. See the note on
    /// TileAccessResolver about why this fails open.
    /// </summary>
    NotConfigured,

    /// <summary>Gate 1 closed: DF Admin has not enabled this tile for the tenant.</summary>
    NotEnabledForTenant,

    /// <summary>Gate 2 closed: the contact's roles are configured, and none grants this tile.</summary>
    RoleLacksTile
}
