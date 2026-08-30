using Hub;
using Hub.Models;
using Hub.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Hub.Tests.Services;

// The tenant's own staff sit on Customer (ClientType 2) clients carrying
// ucclInternal = 1, so taking their ClientType literally resolves them against
// the Customer row set. On urgent-staging that is three tiles.
//
// This only started to matter when the internal-staff branch of Index.cshtml
// began honouring Model.VisibleFeatures. Before that the branch rendered all 17
// tiles unconditionally, so the resolver's answer for an internal caller was
// computed and then thrown away - which is exactly why nobody noticed it was
// wrong, and why Tile Access appeared to do nothing to our own people.
//
// Mapping internal callers to Tenant (4) is what keeps that branch showing what
// it showed before. If this test ever fails, internal staff are about to lose
// most of their hub.
public class FeatureResolverInternalLaneTests
{
    private const int CustomerType = 2;
    private const int TenantType = 4;
    private const int DfAdminType = 5;

    private const string CustomerTile = "hub-tile-customer-only";
    private const string TenantTile = "hub-tile-tenant-only";

    [Fact]
    public async Task An_internal_contact_on_a_customer_client_resolves_as_tenant()
    {
        await using var ctx = NewContext(nameof(An_internal_contact_on_a_customer_client_resolves_as_tenant));
        Seed(ctx, clientId: 1, clientTypeId: CustomerType);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveForClientAsync(clientId: 1, isInternal: true);

        // The whole point: their client says Customer, they get the Tenant set.
        Assert.Contains(TenantTile, keys);
        Assert.DoesNotContain(CustomerTile, keys);
    }

    [Fact]
    public async Task A_real_customer_on_the_same_client_still_resolves_as_customer()
    {
        await using var ctx = NewContext(nameof(A_real_customer_on_the_same_client_still_resolves_as_customer));
        Seed(ctx, clientId: 1, clientTypeId: CustomerType);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Same client record, internal flag absent. Customers must not be
        // promoted into the Tenant tile set by this change.
        var keys = await new FeatureResolver(ctx).ResolveForClientAsync(clientId: 1, isInternal: false);

        Assert.Contains(CustomerTile, keys);
        Assert.DoesNotContain(TenantTile, keys);
    }

    [Fact]
    public async Task The_default_is_not_internal()
    {
        await using var ctx = NewContext(nameof(The_default_is_not_internal));
        Seed(ctx, clientId: 1, clientTypeId: CustomerType);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        // The parameter is optional so existing callers compile unchanged; a
        // caller that forgets it must get the old behaviour, not the new lane.
        var keys = await new FeatureResolver(ctx).ResolveForClientAsync(clientId: 1);

        Assert.Contains(CustomerTile, keys);
        Assert.DoesNotContain(TenantTile, keys);
    }

    [Fact]
    public async Task A_df_admin_keeps_its_bypass_even_when_flagged_internal()
    {
        await using var ctx = NewContext(nameof(A_df_admin_keeps_its_bypass_even_when_flagged_internal));
        Seed(ctx, clientId: 1, clientTypeId: DfAdminType);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Rewriting a DF admin to Tenant would cost them the union bypass and
        // hand them the Tenant slice instead, so ClientType 5 is exempt.
        var keys = await new FeatureResolver(ctx).ResolveForClientAsync(clientId: 1, isInternal: true);

        Assert.Contains(CustomerTile, keys);
        Assert.Contains(TenantTile, keys);
    }

    // ---- helpers ----------------------------------------------------------

    // One tile visible only to Customer, one only to Tenant, so which lane was
    // taken is unambiguous from the result set alone.
    private static void Seed(DynamicDespatchDbContext ctx, int clientId, int clientTypeId)
    {
        Add(ctx, CustomerTile, CustomerType);
        Add(ctx, TenantTile, TenantType);

        // Only UcclId and ClientTypeId matter to the resolver; the rest are
        // non-nullable on the entity and the in-memory provider enforces that.
        ctx.TucClients.Add(new TucClient
        {
            UcclId = clientId,
            ClientTypeId = clientTypeId,
            UcclName = "test client",
            UcclLegalName = "test client",
            UcclCode = "TEST",
            Smsname = "test",
            CreatedBy = "test",
            LastModifiedBy = "test",
        });
    }

    private static void Add(DynamicDespatchDbContext ctx, string key, int clientTypeId)
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

        ctx.ClientTypeFeatures.Add(new ClientTypeFeature
        {
            ClientTypeId = clientTypeId,
            FeatureKey = key,
            Visible = true,
        });
    }

    private static DynamicDespatchDbContext NewContext(string name)
    {
        var options = new DbContextOptionsBuilder<DespatchContext>()
            .UseInMemoryDatabase(name)
            .Options;

        // Never consulted: OnConfiguring short-circuits when options are already set.
        return new DynamicDespatchDbContext(options, Substitute.For<IConnectionStringManager>());
    }
}
