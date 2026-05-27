namespace Hub.Interfaces;

// Phase 5+31 R2 §2 — Hub-side resolver over the dbo.Feature ×
// dbo.ClientTypeFeature matrix added by 20260527090000_FeatureAndClientTypeFeatureMatrix.sql.
//
// Mirrors the configurator's IClientTypeFeatureResolver but adapted to Hub's
// per-request DynamicDespatchDbContext lifecycle (no IDbContextFactory; the
// context is already tenant-scoped via Hub's connection-string switching).
//
// HomeController.Index calls ResolveForClientAsync(clientId, isDfAdmin) on
// each request to derive the user's visible-feature set, which the Razor
// view then consults to decide which hub tiles to render.
//
// DF Admin (UserGroupID=1) bypass returns the union of every visible key
// across all ClientTypes. Belt + braces for any DF admin who hasn't been
// reparented to ClientType=5 yet (still on a legacy Internal or NULL).
//
// Sibling-by-purpose to configurator's resolver — same behaviour but each
// app keeps its own implementation since the EF entities/contexts don't
// cross repo boundaries.
public interface IFeatureResolver
{
    /// <summary>
    /// Returns the visible feature keys for the given ClientType.
    /// NULL → 2 (Customer) per the resolver fallback rule.
    /// </summary>
    Task<HashSet<string>> ResolveVisibleFeaturesAsync(int? clientTypeId);

    /// <summary>
    /// Resolves the visible feature keys for a request: looks up the
    /// supplied client's ClientTypeId, applies the NULL→Customer rule,
    /// and returns the matching matrix slice. DF Admin (isDfAdmin=true)
    /// bypass returns the union of every visible key across all ClientTypes.
    /// </summary>
    Task<HashSet<string>> ResolveForClientAsync(int? clientId, bool isDfAdmin);
}
