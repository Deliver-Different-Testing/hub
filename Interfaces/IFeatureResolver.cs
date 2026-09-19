namespace Hub.Interfaces;

// Phase 5+31 R2 §2 — Hub-side resolver over the dbo.Feature ×
// dbo.ClientTypeFeature matrix added by 20260527090000_FeatureAndClientTypeFeatureMatrix.sql.
//
// Mirrors the configurator's IClientTypeFeatureResolver but adapted to Hub's
// per-request DynamicDespatchDbContext lifecycle (no IDbContextFactory; the
// context is already tenant-scoped via Hub's connection-string switching).
//
// HomeController.Index calls ResolveForClientAsync(clientId) on each request
// to derive the user's visible-feature set, which the Razor view then
// consults to decide which hub tiles to render.
//
// DF Admin (ClientType=5, DFRNTAdmin) bypass returns the union of every
// visible key across all ClientTypes. Keyed on ClientType so a tenant
// Administrator (UserGroupID=1 on a ClientTypeId=4 client) is NOT treated as
// a DF admin — matches the configurator's DF-admin-by-ClientType signal.
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
    /// <param name="countryCode">
    /// The tenant's market, from the <c>CountryCode</c> claim Hub stamps at
    /// login. A feature whose <c>AvailableCountries</c> is set resolves only in
    /// those markets. NULL or empty means DO NOT FILTER, matching the
    /// configurator's deliberate fail-open: hiding a market's whole catalogue
    /// because a claim is missing is worse than briefly over-showing.
    /// </param>
    Task<HashSet<string>> ResolveVisibleFeaturesAsync(int? clientTypeId, string? countryCode = null);

    /// <summary>
    /// Resolves the visible feature keys for a request: looks up the
    /// supplied client's ClientTypeId, applies the NULL→Customer rule,
    /// and returns the matching matrix slice. ClientType=5 (DFRNTAdmin)
    /// bypasses to the union of every visible key across all ClientTypes.
    /// </summary>
    /// <param name="isInternal">
    /// The caller's <c>Internal</c> claim (tucClient.ucclInternal). Internal
    /// staff sit on Customer clients but are the tenant, so they resolve as
    /// ClientType 4 rather than 2. Without this they would resolve against the
    /// Customer row set and lose most of their tiles.
    /// </param>
    /// <param name="countryCode">See <see cref="ResolveVisibleFeaturesAsync"/>.</param>
    Task<HashSet<string>> ResolveForClientAsync(
        int? clientId, bool isInternal = false, string? countryCode = null);
}
