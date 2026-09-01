using Hub;
using Hub.Models;
using Hub.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Hub.Tests.Services;

/// <summary>
/// The DF Admin ceiling (Steve decision note 2026-09-01, step 3).
///
/// Step 2 removed the hardcoded per-audience blocks, so any tile can now reach
/// any audience through the matrix. That is what was asked for, and it also
/// means a TENANT admin could put Admin Manager in front of customers. The
/// ceiling is the safeguard: ClientTypeFeature.Visible is what the tenant
/// granted, Grantable is whether DF Admin allowed them to, and effective
/// visibility is both.
///
/// ENFORCED AT READ TIME, WHICH IS THE POINT OF THESE TESTS. Guarding only the
/// configurator setter would leave every existing grant standing and make the
/// control mean nothing on the day it shipped. Lowering the ceiling has to
/// revoke, not merely block the next click.
/// </summary>
public class FeatureResolverGrantableCeilingTests
{
    private const int TenantType = 4;
    private const int CustomerType = 2;
    private const int DfAdminType = 5;
    private const string Tile = "hub-tile-adminmanager";

    [Fact]
    public async Task A_granted_tile_resolves_while_the_ceiling_is_open()
    {
        await using var ctx = NewContext(nameof(A_granted_tile_resolves_while_the_ceiling_is_open));
        Add(ctx, Tile, CustomerType, visible: true, grantable: true);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Contains(Tile, await new FeatureResolver(ctx).ResolveVisibleFeaturesAsync(CustomerType));
    }

    [Fact]
    public async Task Closing_the_ceiling_revokes_a_tile_the_tenant_had_already_granted()
    {
        // THE IMPORTANT ONE. Visible stays 1 - the tenant bit is kept so that
        // re-opening restores what they had - and the tile still must not
        // resolve. A write-time-only guard would fail this test.
        await using var ctx = NewContext(nameof(Closing_the_ceiling_revokes_a_tile_the_tenant_had_already_granted));
        Add(ctx, Tile, CustomerType, visible: true, grantable: false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(Tile, await new FeatureResolver(ctx).ResolveVisibleFeaturesAsync(CustomerType));
    }

    [Fact]
    public async Task The_ceiling_is_per_client_type_not_per_tile()
    {
        // The whole reason it is a second bit on the row rather than a flag on
        // the feature: Admin Manager for Tenant is ordinary, Admin Manager for
        // Customer is the thing being prevented.
        await using var ctx = NewContext(nameof(The_ceiling_is_per_client_type_not_per_tile));
        Add(ctx, Tile, CustomerType, visible: true, grantable: false);
        Add(ctx, Tile, TenantType, visible: true, grantable: true, addFeature: false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var resolver = new FeatureResolver(ctx);

        Assert.DoesNotContain(Tile, await resolver.ResolveVisibleFeaturesAsync(CustomerType));
        Assert.Contains(Tile, await resolver.ResolveVisibleFeaturesAsync(TenantType));
    }

    [Fact]
    public async Task The_df_admin_union_also_honours_the_ceiling()
    {
        // DF Admin resolves to the union of every visible key. A row the ceiling
        // has closed grants nobody anything, so it must not carry the tile into
        // the union either - otherwise DF Admin gets a tile that no
        // configuration can produce for a real user, and the Tile Access grid
        // reports its own cell inert on the strength of a grant that is not
        // actually in force.
        await using var ctx = NewContext(nameof(The_df_admin_union_also_honours_the_ceiling));
        Add(ctx, Tile, CustomerType, visible: true, grantable: false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveForClientAsync(clientId: null, isInternal: false);

        Assert.DoesNotContain(Tile, keys);
    }

    [Fact]
    public async Task An_open_ceiling_reaches_df_admin_through_the_union()
    {
        // The other half of the pair: the union still works. Without this the
        // test above would pass on a resolver that had simply broken DF Admin.
        await using var ctx = NewContext(nameof(An_open_ceiling_reaches_df_admin_through_the_union));
        Add(ctx, Tile, CustomerType, visible: true, grantable: true);
        // Every one of these is required by the model - the InMemory provider
        // enforces nullability, so a minimal TucClient throws on SaveChanges.
        ctx.TucClients.Add(new TucClient
        {
            UcclId = 900,
            ClientTypeId = DfAdminType,
            UcclName = "Deliver Different",
            UcclLegalName = "Deliver Different Ltd",
            UcclCode = "DFRNT",
            Smsname = "DFRNT",
            CreatedBy = "test",
            LastModifiedBy = "test",
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveForClientAsync(clientId: 900);

        Assert.Contains(Tile, keys);
    }

    [Fact]
    public async Task Grantable_defaults_open_so_the_deploy_changes_nothing()
    {
        // The migration backfills every existing row to 1 and the column
        // defaults to 1. A row written without mentioning Grantable must behave
        // exactly as it did before the column existed.
        await using var ctx = NewContext(nameof(Grantable_defaults_open_so_the_deploy_changes_nothing));
        ctx.Features.Add(new Feature
        {
            FeatureKey = Tile, DisplayName = Tile, Description = "test",
            Category = "HubTile", ClientVisible = true, ReleaseStatus = "Live",
        });
        ctx.ClientTypeFeatures.Add(new ClientTypeFeature
        {
            ClientTypeId = CustomerType, FeatureKey = Tile, Visible = true,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Contains(Tile, await new FeatureResolver(ctx).ResolveVisibleFeaturesAsync(CustomerType));
    }

    [Fact]
    public async Task The_ceiling_composes_with_the_release_gate_rather_than_replacing_it()
    {
        // Two independent ANDs. A tile that is grantable but not Live must stay
        // hidden, or the ceiling would have quietly become the only gate.
        await using var ctx = NewContext(nameof(The_ceiling_composes_with_the_release_gate_rather_than_replacing_it));
        ctx.Features.Add(new Feature
        {
            FeatureKey = Tile, DisplayName = Tile, Description = "test",
            Category = "HubTile", ClientVisible = true, ReleaseStatus = "Draft",
        });
        ctx.ClientTypeFeatures.Add(new ClientTypeFeature
        {
            ClientTypeId = CustomerType, FeatureKey = Tile, Visible = true, Grantable = true,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(Tile, await new FeatureResolver(ctx).ResolveVisibleFeaturesAsync(CustomerType));
    }

    private static void Add(
        DynamicDespatchDbContext ctx, string key, int clientTypeId,
        bool visible, bool grantable, bool addFeature = true)
    {
        if (addFeature)
        {
            ctx.Features.Add(new Feature
            {
                FeatureKey = key,
                DisplayName = key,
                Description = "test",
                Category = "HubTile",
                ClientVisible = true,
                ReleaseStatus = "Live",
            });
        }

        ctx.ClientTypeFeatures.Add(new ClientTypeFeature
        {
            ClientTypeId = clientTypeId,
            FeatureKey = key,
            Visible = visible,
            Grantable = grantable,
        });
    }

    private static DynamicDespatchDbContext NewContext(string name)
    {
        var options = new DbContextOptionsBuilder<DespatchContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DynamicDespatchDbContext(options, Substitute.For<IConnectionStringManager>());
    }
}
