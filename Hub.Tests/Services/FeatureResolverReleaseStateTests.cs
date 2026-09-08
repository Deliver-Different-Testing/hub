using Hub.Models;
using Hub.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Hub.Tests.Services;

// Release state (tile-level direction, 2026-08-26): a feature reaches a tenant
// only when ClientVisible AND ReleaseStatus == "Live".
//
// Hub keeps its own resolver, separate from the configurator's, over the same
// catalogue. These tests exist mainly to stop the two drifting: if Hub applied
// the gate and the configurator did not (or vice versa) the symptom would be a
// hub tile that opens onto an empty sidebar, which is a confusing way to find
// out.
public class FeatureResolverReleaseStateTests
{
    private const int TenantType = 4;
    private const string Live = "hub-tile-live";
    private const string Draft = "hub-tile-draft";
    private const string LiveButKilled = "hub-tile-live-killed";

    [Fact]
    public async Task Only_live_and_client_visible_features_resolve()
    {
        await using var ctx = NewContext(nameof(Only_live_and_client_visible_features_resolve));
        Seed(ctx);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveVisibleFeaturesAsync(TenantType);

        Assert.Contains(Live, keys);

        // Both have ClientTypeFeature.Visible = 1 - the matrix says yes and the
        // release state overrules it.
        Assert.DoesNotContain(Draft, keys);
        Assert.DoesNotContain(LiveButKilled, keys);
    }

    [Fact]
    public async Task Releasing_a_draft_feature_makes_it_resolve()
    {
        await using var ctx = NewContext(nameof(Releasing_a_draft_feature_makes_it_resolve));
        Seed(ctx);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        // AsTracking is required: DynamicDespatchDbContext sets NoTracking for the
        // whole context in OnConfiguring, so a plain read returns a detached
        // entity and mutating it saves nothing - silently, with a green test that
        // asserts the wrong thing.
        var draft = await ctx.Features.AsTracking().SingleAsync(f => f.FeatureKey == Draft,
            TestContext.Current.CancellationToken);
        draft.ClientVisible = true;
        draft.ReleaseStatus = "Live";
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveVisibleFeaturesAsync(TenantType);
        Assert.Contains(Draft, keys);
    }

    // ---- helpers ----------------------------------------------------------

    private static void Seed(DynamicDespatchDbContext ctx)
    {
        Add(ctx, Live, clientVisible: true, status: "Live");
        Add(ctx, Draft, clientVisible: false, status: "Draft");
        Add(ctx, LiveButKilled, clientVisible: false, status: "Live");
    }

    private static void Add(
        DynamicDespatchDbContext ctx, string key, bool clientVisible, string status)
    {
        ctx.Features.Add(new Feature
        {
            FeatureKey = key,
            DisplayName = key,
            Description = "test",
            Category = "HubTile",
            ClientVisible = clientVisible,
            ReleaseStatus = status
        });

        ctx.ClientTypeFeatures.Add(new ClientTypeFeature
        {
            ClientTypeId = TenantType,
            FeatureKey = key,
            Visible = true
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
