using Hub.Models.Master;
using Hub.Repositories;
using Hub.Tests.Helpers;

namespace Hub.Tests.Repositories;

/// <summary>
/// The two master reads behind Shopify merchant sign-in: which user is signing in, and which
/// couriers they may connect a store to.
/// <para>
/// Both are separate from the portal's own lookups on purpose. <c>GetUserByEmailAsync</c> serves a
/// caller that has already been told which kind of account it is looking for; this one has a single
/// login box and must decide for itself, so it cannot inherit that method's tolerance.
/// </para>
/// </summary>
public class ShopifyMerchantSignInRepositoryTests
{
    private static (AuthenticationRepository Repo, MasterContext Context) Create()
    {
        var context = TestMasterContextFactory.CreateWithSeedData();
        return (new AuthenticationRepository(context), context);
    }

    private static void EnableShopify(MasterContext context, params int[] tenantIds)
    {
        foreach (var tenantId in tenantIds)
        {
            context.ShopifyTenantHosts.Add(new ShopifyTenantHost
            {
                TenantId = tenantId,
                IntegrationManagerUrl = $"https://tenant{tenantId}.example.com",
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        context.SaveChanges();
    }

    // ------------------------------------------------------------------ who is signing in

    [Fact]
    public async Task GetShopifyMerchantUser_FindsTheDispatchAccount()
    {
        var (repo, _) = Create();

        var user = await repo.GetShopifyMerchantUserAsync("staff@test.com");

        Assert.NotNull(user);
        Assert.Equal(1, user.UserId);
    }

    /// <summary>
    /// The merchant signing in is a dispatch client contact, matched downstream against
    /// <c>tucClientContact.UserName</c>. A courier operator is a different population in a different
    /// table, and letting their credential through here would mint a merchant link ticket for a
    /// courier's own staff account.
    /// </summary>
    [Fact]
    public async Task GetShopifyMerchantUser_NeverReturnsACourierRow()
    {
        var (repo, _) = Create();

        Assert.Null(await repo.GetShopifyMerchantUserAsync("courier@test.com"));
    }

    /// <summary>
    /// <c>IX_User_Email</c> is unique on (Email, IsCourier), and NULL and 0 are distinct keys under
    /// it — so one email can legitimately have two non-courier rows. An unordered FirstOrDefault
    /// would authenticate whichever one the query plan happened to reach first, which is a different
    /// password on different days.
    /// </summary>
    [Fact]
    public async Task GetShopifyMerchantUser_WithBothANullAndAFalseRow_PicksTheSameOneEveryTime()
    {
        var (repo, context) = Create();

        context.Users.AddRange(
            new User { UserId = 91, Email = "twice@test.com", Password = "B", Salt = "b", IsCourier = null },
            new User { UserId = 90, Email = "twice@test.com", Password = "A", Salt = "a", IsCourier = false });
        context.SaveChanges();

        var first = await repo.GetShopifyMerchantUserAsync("twice@test.com");
        var second = await repo.GetShopifyMerchantUserAsync("twice@test.com");

        Assert.NotNull(first);
        Assert.Equal(90, first.UserId);
        Assert.Equal(first.UserId, second!.UserId);
    }

    [Fact]
    public async Task GetShopifyMerchantUser_WithAnUnknownEmail_ReturnsNull()
    {
        var (repo, _) = Create();

        Assert.Null(await repo.GetShopifyMerchantUserAsync("nobody@test.com"));
    }

    /// <summary>
    /// Tracked, because a legacy row is rehashed in place on a successful sign-in.
    /// </summary>
    [Fact]
    public async Task GetShopifyMerchantUser_IsTracked_SoTheLegacyRehashCanBeSaved()
    {
        var (repo, context) = Create();

        var user = await repo.GetShopifyMerchantUserAsync("legacy@test.com");
        user!.IsLegacyHash = false;
        await repo.SaveAsync();

        Assert.False(context.Users.Single(u => u.UserId == 3).IsLegacyHash);
    }

    // ------------------------------------------------------------------ which couriers

    /// <summary>
    /// A row in ShopifyTenantHost is what "this courier is switched on for Shopify" means. A tenant
    /// the user belongs to without one is not an option, and must not even be named — the response
    /// goes to the public front door, and listing them would hand it a map of the merchant's other
    /// courier relationships.
    /// </summary>
    [Fact]
    public async Task GetShopifyTenantsForUser_ListsOnlyTheCouriersWithShopifySwitchedOn()
    {
        var (repo, context) = Create();
        EnableShopify(context, 1);

        var tenants = await repo.GetShopifyTenantsForUserAsync(1);

        Assert.Equal(1, Assert.Single(tenants).TenantId);
    }

    [Fact]
    public async Task GetShopifyTenantsForUser_ListsEveryEnabledCourierTheUserBelongsTo()
    {
        var (repo, context) = Create();
        EnableShopify(context, 1, 2);

        var tenants = await repo.GetShopifyTenantsForUserAsync(1);

        Assert.Equal([1, 2], tenants.Select(t => t.TenantId).Order());
    }

    [Fact]
    public async Task GetShopifyTenantsForUser_CarriesTheCodeNameAndHost()
    {
        var (repo, context) = Create();
        EnableShopify(context, 1);

        var tenant = Assert.Single(await repo.GetShopifyTenantsForUserAsync(1));

        Assert.Equal("test", tenant.Code);
        Assert.Equal("Test Tenant", tenant.Name);
        Assert.Equal("https://tenant1.example.com", tenant.Host);
    }

    [Fact]
    public async Task GetShopifyTenantsForUser_WithNothingSwitchedOn_IsEmpty()
    {
        var (repo, _) = Create();

        Assert.Empty(await repo.GetShopifyTenantsForUserAsync(1));
    }

    [Fact]
    public async Task GetShopifyTenantsForUser_ForAUserInNoTenant_IsEmpty()
    {
        var (repo, context) = Create();
        EnableShopify(context, 1, 2);

        Assert.Empty(await repo.GetShopifyTenantsForUserAsync(999));
    }

    /// <summary>
    /// Membership is the gate, not the host row: belonging to no tenant with Shopify on is the same
    /// answer as belonging to no tenant at all.
    /// </summary>
    [Fact]
    public async Task GetShopifyTenantsForUser_DoesNotListACourierTheUserDoesNotBelongTo()
    {
        var (repo, context) = Create();
        EnableShopify(context, 1, 2);

        var tenants = await repo.GetShopifyTenantsForUserAsync(2);

        Assert.Equal(1, Assert.Single(tenants).TenantId);
    }

    // ------------------------------------------------------------------ switching a courier on

    [Fact]
    public async Task UpsertShopifyTenantHost_SwitchesACourierOn()
    {
        var (repo, context) = Create();

        Assert.True(await repo.UpsertShopifyTenantHostAsync(1, "https://urgent.example.com"));

        Assert.Equal("https://urgent.example.com",
            context.ShopifyTenantHosts.Single(h => h.TenantId == 1).IntegrationManagerUrl);
    }

    /// <summary>
    /// A deployment calls this every time it starts, so the second call must be an update rather than
    /// a duplicate-key failure - and its address really can change.
    /// </summary>
    [Fact]
    public async Task UpsertShopifyTenantHost_CalledAgain_MovesTheCourierRatherThanFailing()
    {
        var (repo, context) = Create();
        await repo.UpsertShopifyTenantHostAsync(1, "https://old.example.com");

        Assert.True(await repo.UpsertShopifyTenantHostAsync(1, "https://new.example.com"));

        var host = Assert.Single(context.ShopifyTenantHosts.Where(h => h.TenantId == 1));
        Assert.Equal("https://new.example.com", host.IntegrationManagerUrl);
    }

    [Fact]
    public async Task UpsertShopifyTenantHost_ForATenantThatDoesNotExist_WritesNothing()
    {
        var (repo, context) = Create();

        Assert.False(await repo.UpsertShopifyTenantHostAsync(999, "https://nowhere.example.com"));

        Assert.Empty(context.ShopifyTenantHosts);
    }

    /// <summary>
    /// The whole point of the join: a courier switched on becomes reachable to the merchants who
    /// belong to them, and only to those.
    /// </summary>
    [Fact]
    public async Task UpsertShopifyTenantHost_MakesTheCourierAppearToItsOwnMerchants()
    {
        var (repo, _) = Create();
        Assert.Empty(await repo.GetShopifyTenantsForUserAsync(1));

        await repo.UpsertShopifyTenantHostAsync(1, "https://urgent.example.com");

        Assert.Equal(1, Assert.Single(await repo.GetShopifyTenantsForUserAsync(1)).TenantId);
    }
}
