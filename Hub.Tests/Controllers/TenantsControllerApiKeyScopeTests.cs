using Hub.Controllers;
using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Controllers;

/// <summary>
/// Which key opens which door.
/// <para>
/// Master holds every tenant's database credentials, and the Deliver DFRNT setup service is a public
/// Shopify app URL - reachable by any store on the internet, including ones that never installed.
/// It needs three things from here and nothing else, so its key is worth exactly those three. The
/// existing PartnerDirectoryApiKey keeps opening everything, because staff tooling and the per-tenant
/// Integration Manager deployments already use it.
/// </para>
/// </summary>
[Collection("PartnerDirectoryApiKey")]
public class TenantsControllerApiKeyScopeTests : IDisposable
{
    private const string FullKey = "full-key-abc";
    private const string SetupKey = "setup-key-xyz";
    private const string Shop = "a-store.myshopify.com";

    private readonly IAuthenticationRepository _repository = Substitute.For<IAuthenticationRepository>();
    private readonly TenantsController _controller;

    public TenantsControllerApiKeyScopeTests()
    {
        _controller = new TenantsController(_repository);
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", FullKey);
        Environment.SetEnvironmentVariable("ShopifySetupApiKey", SetupKey);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", null);
        Environment.SetEnvironmentVariable("ShopifySetupApiKey", null);
    }

    // ---- The three the setup service is for -------------------------------------------------

    /// <summary>
    /// A merchant arrives at the setup service carrying nothing but a shop domain, so this is the
    /// first question it asks - is this store already somebody's?
    /// </summary>
    [Fact]
    public async Task SetupKey_MayAskWhichTenantOwnsAShop()
    {
        var result = await _controller.GetByShopifyShop(SetupKey, Shop);

        Assert.IsNotType<UnauthorizedResult>(result);
        await _repository.Received(1).GetTenantByShopifyShopAsync(Shop);
    }

    /// <summary>
    /// Built for Shopify requires a merchant be able to sever the connection from inside the
    /// embedded app, and the embedded app is the setup service.
    /// </summary>
    [Fact]
    public async Task SetupKey_MayDisconnectAShop()
    {
        var result = await _controller.UnmapShopifyShop(SetupKey, 7, Shop);

        Assert.IsType<NoContentResult>(result);
        await _repository.Received(1).UnmapShopifyShopAsync(Shop, 7);
    }

    // ---- Everything else ---------------------------------------------------------------------

    /// <summary>
    /// The one that matters. A connection string is credentials for a tenant's whole database, and
    /// handing them to the most exposed process in the system is the thing this scoping exists to
    /// prevent.
    /// </summary>
    [Fact]
    public async Task SetupKey_IsRefusedATenantConnectionString()
    {
        var result = await _controller.GetConnectionString(SetupKey, 7);

        Assert.IsType<UnauthorizedResult>(result);
        await _repository.DidNotReceive().GetTenantConnectionStringAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task SetupKey_IsRefusedATenantTimeZone()
    {
        var result = await _controller.GetTimeZone(SetupKey, 7);

        Assert.IsType<UnauthorizedResult>(result);
        await _repository.DidNotReceive().GetTenantTimeZoneAsync(Arg.Any<int>());
    }

    /// <summary>
    /// The front door writes the shop→tenant mapping itself, straight into master, once a sign-in has
    /// settled which courier a store belongs to. It never asks Hub to write one - so if it could,
    /// that would be a way to attach any store to any tenant with no sign-in behind it.
    /// </summary>
    [Fact]
    public async Task SetupKey_CannotMapAShopToATenant()
    {
        var result = await _controller.PutShopifyShop(SetupKey, 7,
            new ShopifyShopTenantRequest { Shop = Shop });

        Assert.IsType<UnauthorizedResult>(result);
        await _repository.DidNotReceive().MapShopifyShopAsync(Arg.Any<string>(), Arg.Any<int>());
    }

    /// <summary>
    /// Where a courier's Integration Manager lives is what routes every merchant on that tenant.
    /// Letting the most exposed service in the estate rewrite it would let it point a courier's
    /// merchants at a host of its choosing.
    /// </summary>
    [Fact]
    public async Task SetupKey_CannotMoveACouriersIntegrationManager()
    {
        var result = await _controller.PutShopifyHost(SetupKey, 7,
            new ShopifyTenantHostRequest { IntegrationManagerUrl = "https://elsewhere.example.com" });

        Assert.IsType<UnauthorizedResult>(result);
        await _repository.DidNotReceive().UpsertShopifyTenantHostAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    // ---- The full key is unchanged -----------------------------------------------------------

    [Fact]
    public async Task FullKey_StillOpensTheSetupEndpointsToo()
    {
        var result = await _controller.GetByShopifyShop(FullKey, Shop);

        Assert.IsNotType<UnauthorizedResult>(result);
        await _repository.Received(1).GetTenantByShopifyShopAsync(Shop);
    }

    [Fact]
    public async Task FullKey_StillOpensTheRest()
    {
        _repository.GetTenantConnectionStringAsync(7).Returns("Server=x;Database=y;");

        var result = await _controller.GetConnectionString(FullKey, 7);

        Assert.IsType<OkObjectResult>(result);
    }

    // ---- Absent configuration ----------------------------------------------------------------

    /// <summary>
    /// An unset key must close the door, not open it to everyone. Comparing against an empty
    /// expected value would admit a caller sending nothing at all.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WithNoSetupKeyConfigured_TheSetupEndpointsRefuseThatKey(string? configured)
    {
        Environment.SetEnvironmentVariable("ShopifySetupApiKey", configured);

        Assert.IsType<UnauthorizedResult>(await _controller.GetByShopifyShop("", Shop));
        Assert.IsType<UnauthorizedResult>(await _controller.GetByShopifyShop(null, Shop));

        await _repository.DidNotReceive().GetTenantByShopifyShopAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task AKeyThatIsNeither_OpensNothing()
    {
        Assert.IsType<UnauthorizedResult>(await _controller.GetByShopifyShop("guessed", Shop));
        Assert.IsType<UnauthorizedResult>(await _controller.GetConnectionString("guessed", 7));

        await _repository.DidNotReceive().GetTenantByShopifyShopAsync(Arg.Any<string>());
        await _repository.DidNotReceive().GetTenantConnectionStringAsync(Arg.Any<int>());
    }
}
