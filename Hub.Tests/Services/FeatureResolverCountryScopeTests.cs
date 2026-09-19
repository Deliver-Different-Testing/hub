using Hub.Models;
using Hub.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Hub.Tests.Services;

/// <summary>
/// Country scope (SEED-SCOPE-ALL-HUBS §2) and the DF Admin union's gates.
///
/// Hub had neither. Feature.AvailableCountries is written from the Feature
/// Matrix and was filtered by the configurator's ClientTypeFeatureResolver but
/// not here, so an NZ-scoped feature vanished from an AU tenant's sidebar while
/// its hub tile still rendered - the two resolvers disagreeing in exactly the
/// way their own comments warn about. And the DF Admin branch selected from
/// ClientTypeFeature alone, so an admin saw Draft and Beta tiles nobody else
/// could, in every market.
///
/// These pin both, because both were silent: nothing errored, a tile simply
/// appeared where it should not have.
/// </summary>
public class FeatureResolverCountryScopeTests
{
    private const int CustomerType = 2;
    private const int TenantType = 4;
    private const int DfAdminType = 5;

    private const string Global = "hub-tile-booking";
    private const string NzOnly = "hub-tile-fuel-surcharge";

    [Fact]
    public async Task A_country_scoped_feature_resolves_in_its_own_market()
    {
        await using var ctx = NewContext(nameof(A_country_scoped_feature_resolves_in_its_own_market));
        Add(ctx, NzOnly, CustomerType, countries: "NZ");
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveVisibleFeaturesAsync(CustomerType, "NZ");

        Assert.Contains(NzOnly, keys);
    }

    [Fact]
    public async Task A_country_scoped_feature_does_not_resolve_elsewhere()
    {
        // THE IMPORTANT ONE. Visible, Grantable and Live are all true - only the
        // market differs, and before this the tile rendered anyway.
        await using var ctx = NewContext(nameof(A_country_scoped_feature_does_not_resolve_elsewhere));
        Add(ctx, NzOnly, CustomerType, countries: "NZ");
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveVisibleFeaturesAsync(CustomerType, "AU");

        Assert.DoesNotContain(NzOnly, keys);
    }

    [Theory]
    [InlineData("NZ,AU")]
    [InlineData("nz, au")]
    [InlineData(" AU ,NZ ")]
    public async Task The_list_is_comma_separated_trimmed_and_case_insensitive(string countries)
    {
        await using var ctx = NewContext(nameof(The_list_is_comma_separated_trimmed_and_case_insensitive) + countries);
        Add(ctx, NzOnly, CustomerType, countries: countries);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveVisibleFeaturesAsync(CustomerType, "AU");

        Assert.Contains(NzOnly, keys);
    }

    [Theory]
    [InlineData("null", null)]
    [InlineData("empty", "")]
    public async Task A_feature_with_no_country_set_is_global(string store, string? countries)
    {
        // `store` only names the in-memory database: null and "" would
        // otherwise collide on one store and the second case would insert a
        // duplicate key.
        await using var ctx = NewContext(nameof(A_feature_with_no_country_set_is_global) + store);
        Add(ctx, Global, CustomerType, countries: countries);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveVisibleFeaturesAsync(CustomerType, "AU");

        Assert.Contains(Global, keys);
    }

    [Theory]
    [InlineData("null", null)]
    [InlineData("empty", "")]
    public async Task An_unknown_country_fails_open_rather_than_hiding_the_catalogue(
        string store, string? countryCode)
    {
        // Deliberate, and it matches the configurator exactly: a missing
        // CountryCode claim - a stale pre-claim cookie - must not blank the
        // whole market's catalogue for a legitimate user.
        await using var ctx = NewContext(nameof(An_unknown_country_fails_open_rather_than_hiding_the_catalogue) + store);
        Add(ctx, NzOnly, CustomerType, countries: "NZ");
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveVisibleFeaturesAsync(CustomerType, countryCode);

        Assert.Contains(NzOnly, keys);
    }

    [Fact]
    public async Task The_df_admin_union_applies_the_release_gate()
    {
        // Was the gap: the admin branch joined nothing, so a Draft tile any
        // client type had ticked reached an admin and nobody else.
        await using var ctx = NewContext(nameof(The_df_admin_union_applies_the_release_gate));
        Add(ctx, Global, TenantType, countries: null);
        Add(ctx, "hub-tile-dfrntcrm", TenantType, countries: null, releaseStatus: "Draft");
        AddDfAdminClient(ctx);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveForClientAsync(clientId: 900);

        Assert.Contains(Global, keys);
        Assert.DoesNotContain("hub-tile-dfrntcrm", keys);
    }

    [Fact]
    public async Task The_df_admin_union_applies_country_scope()
    {
        await using var ctx = NewContext(nameof(The_df_admin_union_applies_country_scope));
        Add(ctx, Global, TenantType, countries: null);
        Add(ctx, NzOnly, TenantType, countries: "NZ");
        AddDfAdminClient(ctx);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx)
            .ResolveForClientAsync(clientId: 900, isInternal: false, countryCode: "AU");

        Assert.Contains(Global, keys);
        Assert.DoesNotContain(NzOnly, keys);
    }

    [Fact]
    public async Task The_df_admin_union_still_spans_every_client_type()
    {
        // The bypass it is supposed to be: one tile ticked only for Customer,
        // another only for Tenant, and an admin gets both.
        await using var ctx = NewContext(nameof(The_df_admin_union_still_spans_every_client_type));
        Add(ctx, Global, CustomerType, countries: null);
        Add(ctx, "hub-tile-adminmanager", TenantType, countries: null);
        AddDfAdminClient(ctx);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var keys = await new FeatureResolver(ctx).ResolveForClientAsync(clientId: 900);

        Assert.Contains(Global, keys);
        Assert.Contains("hub-tile-adminmanager", keys);
    }

    // ---- helpers ----------------------------------------------------------

    private static void Add(
        DynamicDespatchDbContext ctx, string key, int clientTypeId,
        string? countries, string releaseStatus = "Live")
    {
        ctx.Features.Add(new Feature
        {
            FeatureKey = key,
            DisplayName = key,
            Description = "test",
            Category = "HubTile",
            ClientVisible = true,
            ReleaseStatus = releaseStatus,
            AvailableCountries = countries
        });

        ctx.ClientTypeFeatures.Add(new ClientTypeFeature
        {
            ClientTypeId = clientTypeId,
            FeatureKey = key,
            Visible = true,
            Grantable = true
        });
    }

    private static void AddDfAdminClient(DynamicDespatchDbContext ctx)
    {
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
            LastModifiedBy = "test"
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
