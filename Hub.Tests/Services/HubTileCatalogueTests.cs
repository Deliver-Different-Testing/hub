using Hub.Services;

namespace Hub.Tests.Services;

/// <summary>
/// The launcher tile list.
///
/// This replaced five hardcoded per-audience blocks in Index.cshtml on
/// 2026-09-01. Those blocks were the reason Tile Access could only ever HIDE a
/// tile an audience already had and never grant one — ticking Settings for
/// Customer saved correctly and showed nothing, because the customer block had
/// no Settings markup to reveal. A customer could reach 6 of 17 tiles whatever
/// the configuration said.
///
/// The first test is the one that proves that is over.
/// </summary>
public class HubTileCatalogueTests
{
    /// <summary>Nothing withheld — every non-matrix condition satisfied.</summary>
    private static HubTileContext Permissive() => new(
        AppUrl: app => $"https://{app}.test",
        FuelSurchargeUrl: "/FuelSurcharge",
        BookingPath: "/#/login/",
        HasBulkUploadPermission: true,
        ShowFuelSurcharge: true,
        IsAsureUser: false);

    private static HashSet<string> AllKeys() =>
        new(HubTileCatalogue.Keys, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Any_tile_can_reach_any_audience()
    {
        // THE POINT OF THE WHOLE CHANGE. Given the matrix allows everything,
        // everything renders — there is no audience the code can still refuse.
        // Before this, the answer depended on which hardcoded block you landed
        // in, and for a customer it was capped at six.
        var tiles = HubTileCatalogue.Resolve(Permissive(), AllKeys());

        Assert.Equal(HubTileCatalogue.Keys.Count, tiles.Count);
        Assert.Contains(tiles, t => t.Key == "hub-tile-configurator");
        Assert.Contains(tiles, t => t.Key == "hub-tile-adminmanager");
    }

    [Fact]
    public void A_tile_the_matrix_withholds_does_not_render()
    {
        var allowed = AllKeys();
        allowed.Remove("hub-tile-configurator");

        var tiles = HubTileCatalogue.Resolve(Permissive(), allowed);

        Assert.DoesNotContain(tiles, t => t.Key == "hub-tile-configurator");
    }

    [Fact]
    public void An_empty_feature_set_renders_nothing()
    {
        // Couriers arrive here with no ClientType and therefore no features. The
        // view keeps its own hardcoded courier block; this must not quietly fill
        // the gap with everything.
        Assert.Empty(HubTileCatalogue.Resolve(Permissive(), new HashSet<string>()));
    }

    // ── The conditions the matrix cannot express ──────────────────────────

    [Fact]
    public void Bulk_import_still_needs_the_legacy_permission()
    {
        // AND, never a swap: the matrix can take Bulk Import away, but it must
        // not hand it to someone InternetPermission 11 excluded.
        var ctx = Permissive() with { HasBulkUploadPermission = false };

        var tiles = HubTileCatalogue.Resolve(ctx, AllKeys());

        Assert.DoesNotContain(tiles, t => t.Key == "hub-tile-bulk-import");
    }

    [Fact]
    public void Fuel_surcharge_is_urgent_only()
    {
        // Not a permission — the page does not exist on other tenants, so no
        // setting could make it work.
        var ctx = Permissive() with { ShowFuelSurcharge = false };

        Assert.DoesNotContain(HubTileCatalogue.Resolve(ctx, AllKeys()),
            t => t.Key == "hub-tile-fuel-surcharge");
    }

    [Fact]
    public void The_asure_account_still_loses_print_and_route_viewer()
    {
        // A per-USER rule, which is why it cannot live in a per-audience matrix.
        var ctx = Permissive() with { IsAsureUser = true };

        var tiles = HubTileCatalogue.Resolve(ctx, AllKeys());

        Assert.DoesNotContain(tiles, t => t.Key == "hub-tile-print");
        Assert.DoesNotContain(tiles, t => t.Key == "hub-tile-routeviewer");
        // and nothing else is affected
        Assert.Contains(tiles, t => t.Key == "hub-tile-booking");
    }

    // ── Rendering details the view depends on ─────────────────────────────

    [Fact]
    public void The_coming_soon_tile_carries_no_link()
    {
        var csp = HubTileCatalogue.Resolve(Permissive(), AllKeys())
            .Single(t => t.Key == "hub-tile-dfrntcrm");

        // The view renders a div rather than an anchor for these. A stray href
        // would make an unreleased product clickable.
        Assert.True(csp.IsDisabled);
        Assert.Equal(string.Empty, csp.Href);
    }

    [Fact]
    public void Every_enabled_tile_has_a_destination_and_an_icon()
    {
        foreach (var tile in HubTileCatalogue.Resolve(Permissive(), AllKeys()).Where(t => !t.IsDisabled))
        {
            Assert.False(string.IsNullOrWhiteSpace(tile.Href), $"{tile.Key} has no href");
            Assert.False(string.IsNullOrWhiteSpace(tile.Icon), $"{tile.Key} has no icon");
            Assert.False(string.IsNullOrWhiteSpace(tile.Title), $"{tile.Key} has no title");
        }
    }

    [Fact]
    public void The_booking_path_is_appended_for_the_asure_landing_route()
    {
        var ctx = Permissive() with { BookingPath = "/#/asure" };

        var booking = HubTileCatalogue.Resolve(ctx, AllKeys()).Single(t => t.Key == "hub-tile-booking");

        Assert.EndsWith("/#/asure", booking.Href);
    }

    [Fact]
    public void Keys_are_unique_and_all_hub_tile_keys()
    {
        // The prefix is a contract, not a convention: the configurator's
        // navGovernance test fails the build if a non hub-tile-* key sits at the
        // root of the feature tree, and TileAccessResolver matches on it.
        Assert.Equal(HubTileCatalogue.Keys.Count, HubTileCatalogue.Keys.Distinct().Count());
        Assert.All(HubTileCatalogue.Keys, k => Assert.StartsWith("hub-tile-", k));
    }

    [Fact]
    public void Order_is_stable_between_calls()
    {
        // The view renders in list order and site.less staggers on :nth-child,
        // so an unstable order would reshuffle the grid between page loads.
        Assert.Equal(
            HubTileCatalogue.Resolve(Permissive(), AllKeys()).Select(t => t.Key),
            HubTileCatalogue.Resolve(Permissive(), AllKeys()).Select(t => t.Key));
    }
}
