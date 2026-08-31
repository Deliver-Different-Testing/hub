using Hub.Models;

namespace Hub.Services;

/// <summary>
/// Everything Hub needs to decide which launcher tiles a caller gets, other
/// than their resolved feature keys.
/// </summary>
/// <param name="AppUrl">Builds a per-tenant app URL from an app slug.</param>
/// <param name="FuelSurchargeUrl">
/// Fuel Surcharge is a page in Hub itself rather than another app, so its URL
/// comes from routing rather than the tenant URL template.
/// </param>
/// <param name="BookingPath">
/// Appended to the booking app URL. Differs for the Asure account, which has
/// its own landing route.
/// </param>
/// <param name="HasBulkUploadPermission">Legacy InternetPermission 11.</param>
/// <param name="ShowFuelSurcharge">
/// True only on the urgent tenant. The page does not exist elsewhere, so no
/// matrix setting can make it work.
/// </param>
/// <param name="IsAsureUser">
/// One specific account on the urgent tenant that must not see Print or Route
/// Viewer. A per-USER rule rather than a per-audience one, which is why it
/// cannot move into the matrix.
/// </param>
public sealed record HubTileContext(
    Func<string, string> AppUrl,
    string FuelSurchargeUrl,
    string BookingPath,
    bool HasBulkUploadPermission,
    bool ShowFuelSurcharge,
    bool IsAsureUser);

/// <summary>
/// The one place that knows what a hub tile is.
/// </summary>
/// <remarks>
/// REPLACES FIVE HARDCODED AUDIENCE BLOCKS (Steve decision note 2026-09-01).
///
/// Index.cshtml used to carry a separate block per audience, each listing the
/// tiles someone had written into it. The matrix could only ever HIDE a tile a
/// block already contained, never add one - so ticking Settings for Customer
/// did nothing, because the customer block had no Settings markup to reveal.
/// A customer could reach 6 of 17 tiles no matter what Tile Access said.
///
/// Now there is one list and the matrix decides. "Which tiles does this
/// audience get" is answered entirely by ClientTypeFeature, which is what the
/// Tile Access screen has been claiming all along.
///
/// COURIERS ARE STILL SPECIAL, AND HAVE TO BE. A courier has no ClientType, so
/// FeatureResolver returns an empty set for them and there is no audience row
/// to configure. Their three tiles stay hardcoded in the view. Making couriers
/// a real audience means giving them a client type, which is a bigger decision
/// than this change.
///
/// THE FOUR NON-MATRIX CONDITIONS BELOW ARE NOT GATING GAPS. They are real
/// runtime facts - a missing legacy permission, a page that only exists on one
/// tenant, one account with a bespoke rule. A ticked box stays necessary but
/// not always sufficient, and the Tile Access UI says so rather than pretending
/// otherwise.
/// </remarks>
public static class HubTileCatalogue
{
    private sealed record Definition(
        string Key,
        string Title,
        string Icon,
        Func<HubTileContext, string> Href,
        Func<HubTileContext, bool> Available,
        bool IsDisabled = false);

    /// <summary>Every tile Hub knows how to render, in display order.</summary>
    public static IReadOnlyList<string> Keys => Definitions.Select(d => d.Key).ToList();

    public static IReadOnlyList<HubTile> Resolve(HubTileContext ctx, ISet<string> visibleFeatures)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(visibleFeatures);

        return Definitions
            .Where(d => visibleFeatures.Contains(d.Key) && d.Available(ctx))
            .Select(d => new HubTile(
                d.Key, d.Title, d.Icon, d.IsDisabled ? string.Empty : d.Href(ctx), d.IsDisabled))
            .ToList();
    }

    /// <summary>
    /// Every tile the matrix can grant, in display order.
    /// </summary>
    /// <remarks>
    /// Order is fixed here rather than read from Feature.SortOrder: Hub resolves
    /// feature KEYS only, and adding an ordering round-trip to render a launcher
    /// is not worth it. Alphabetical by title, matching what internal staff have
    /// been looking at for months.
    ///
    /// The count matters beyond aesthetics — site.less staggers the entry
    /// animation per :nth-child, so a tile added here without extending that
    /// rule pops in with the first card instead of trailing. There is a test.
    /// </remarks>
    private static readonly Definition[] Definitions =
        [
            new Definition("hub-tile-accounts", "Accounts", "accounts.svg",
                ctx => ctx.AppUrl("accounts"),
                ctx => true),

            new Definition("hub-tile-adminmanager", "Admin Manager", "admin-manager.svg",
                ctx => ctx.AppUrl("adminmanager"),
                ctx => true),

            new Definition("hub-tile-booking", "Booking/Job List", "booking-page.svg",
                ctx => ctx.AppUrl("booking") + ctx.BookingPath,
                ctx => true),

            // The matrix can take Bulk Import away but must never hand it to
            // someone the legacy permission excluded, so this is AND.
            new Definition("hub-tile-bulk-import", "Bulk Import", "bulk-import.svg",
                ctx => ctx.AppUrl("bulkimport") + "/#/login/",
                ctx => ctx.HasBulkUploadPermission),

            new Definition("hub-tile-client-manager", "Client Manager", "client-manager.svg",
                ctx => ctx.AppUrl("clientmanager"),
                ctx => true),

            new Definition("hub-tile-couriermanager", "Courier Manager", "courier-manager.svg",
                ctx => ctx.AppUrl("couriermanager"),
                ctx => true),

            new Definition("hub-tile-courier-portal", "Courier Portal", "courier-portal.svg",
                ctx => ctx.AppUrl("courierportal"),
                ctx => true),

            // Shown so people know it is coming, but not clickable - the product
            // is not ready. Swap Disabled to false and give it a URL to release.
            new Definition("hub-tile-dfrntcrm", "Customer Success Platform", "crm-inbox.svg",
                ctx => string.Empty,
                ctx => true,
                IsDisabled: true),

            new Definition("hub-tile-despatchweb", "Dispatch", "dispatch.svg",
                ctx => ctx.AppUrl("despatch"),
                ctx => true),

            // Only exists on the urgent tenant. Not a permission - the page is
            // simply not there anywhere else.
            new Definition("hub-tile-fuel-surcharge", "Fuel Surcharge", "fuel icon.svg",
                ctx => ctx.FuelSurchargeUrl,
                ctx => ctx.ShowFuelSurcharge),

            new Definition("hub-tile-integration-manager", "Integration Manager", "integration-hub.svg",
                ctx => ctx.AppUrl("integrationmanager") + "/auth/entry",
                ctx => true),

            new Definition("hub-tile-tracking", "Job Search", "tracker.svg",
                ctx => ctx.AppUrl("tracking"),
                ctx => true),

            new Definition("hub-tile-print", "Print", "print.svg",
                ctx => ctx.AppUrl("runviewer") + "/#/print",
                ctx => !ctx.IsAsureUser),

            new Definition("hub-tile-route-builder", "Route Builder", "route-builder.svg",
                ctx => ctx.AppUrl("runbuilder"),
                ctx => true),

            // The Route Builder successor, running alongside it until parity is
            // signed off.
            new Definition("hub-tile-routed-operations", "Routed Operations", "route-builder.svg",
                ctx => ctx.AppUrl("routedoperations"),
                ctx => true),

            new Definition("hub-tile-routeviewer", "Route Viewer", "route-viewer.svg",
                ctx => ctx.AppUrl("runviewer"),
                ctx => !ctx.IsAsureUser),

            // Renamed from Workflows 2026-08-31. Couriers and NPs used to see
            // this same tile labelled "DFRNT Drive"; one list means one label,
            // and Steve confirmed on 2026-09-01 that the label is not the point.
            new Definition("hub-tile-configurator", "Settings", "workflow.png",
                ctx => ctx.AppUrl("dfrntdriveconfig"),
                ctx => true),
        ];
}

