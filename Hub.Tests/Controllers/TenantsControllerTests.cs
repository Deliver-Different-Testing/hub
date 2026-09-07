using Hub.Controllers;
using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Hub.Tests.Controllers;

[Collection("PartnerDirectoryApiKey")]
public class TenantsControllerTests : IDisposable
{
    private readonly IAuthenticationRepository _mockRepository;
    private readonly TenantsController _controller;
    private const string ValidApiKey = "test-api-key-123";

    public TenantsControllerTests()
    {
        _mockRepository = Substitute.For<IAuthenticationRepository>();
        _controller = new TenantsController(_mockRepository);
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", ValidApiKey);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", null);
    }

    [Fact]
    public async Task GetConnectionString_ValidApiKey_Returns200WithConnection()
    {
        const string connectionString = "Server=hub.test;Database=Tenant42;";
        _mockRepository.GetTenantConnectionStringAsync(42).Returns(connectionString);

        var result = await _controller.GetConnectionString(ValidApiKey, 42);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TenantConnectionStringResponse>(okResult.Value);
        Assert.Equal(42, response.TenantId);
        Assert.Equal(connectionString, response.ConnectionString);
    }

    [Fact]
    public async Task GetConnectionString_MissingApiKey_Returns401()
    {
        var result = await _controller.GetConnectionString(null, 42);

        Assert.IsType<UnauthorizedResult>(result);
        await _mockRepository.DidNotReceive().GetTenantConnectionStringAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task GetConnectionString_WrongApiKey_Returns401()
    {
        var result = await _controller.GetConnectionString("wrong-key", 42);

        Assert.IsType<UnauthorizedResult>(result);
        await _mockRepository.DidNotReceive().GetTenantConnectionStringAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task GetConnectionString_EnvVarNotSet_Returns401()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", null);

        var result = await _controller.GetConnectionString(ValidApiKey, 42);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetConnectionString_TenantNotFound_Returns404()
    {
        _mockRepository.GetTenantConnectionStringAsync(999).Returns((string?)null);

        var result = await _controller.GetConnectionString(ValidApiKey, 999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetConnectionString_EmptyConnectionString_Returns404()
    {
        _mockRepository.GetTenantConnectionStringAsync(7).Returns(string.Empty);

        var result = await _controller.GetConnectionString(ValidApiKey, 7);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetConnectionString_RepositoryThrows_Returns500()
    {
        _mockRepository.GetTenantConnectionStringAsync(1).ThrowsAsync(new Exception("db blew up"));

        var result = await _controller.GetConnectionString(ValidApiKey, 1);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    // Integration Manager hosts the Shopify order-polling and auto-fulfilment sweeps, which
    // evaluate time-based booking rules against tenant-local now. It reads every other piece of
    // tenant metadata through Hub, so it reads the timezone here rather than opening its own
    // master-controller connection.
    [Fact]
    public async Task GetTimeZone_ValidApiKey_Returns200WithZone()
    {
        _mockRepository.GetTenantTimeZoneAsync(42).Returns("New Zealand Standard Time");

        var result = await _controller.GetTimeZone(ValidApiKey, 42);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TenantTimeZoneResponse>(okResult.Value);
        Assert.Equal(42, response.TenantId);
        Assert.Equal("New Zealand Standard Time", response.TimeZone);
    }

    [Fact]
    public async Task GetTimeZone_MissingApiKey_Returns401()
    {
        var result = await _controller.GetTimeZone(null, 42);

        Assert.IsType<UnauthorizedResult>(result);
        await _mockRepository.DidNotReceive().GetTenantTimeZoneAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task GetTimeZone_WrongApiKey_Returns401()
    {
        var result = await _controller.GetTimeZone("wrong-key", 42);

        Assert.IsType<UnauthorizedResult>(result);
        await _mockRepository.DidNotReceive().GetTenantTimeZoneAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task GetTimeZone_TenantNotFound_Returns404()
    {
        _mockRepository.GetTenantTimeZoneAsync(999).Returns((string?)null);

        var result = await _controller.GetTimeZone(ValidApiKey, 999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetTimeZone_TenantWithNoZoneRecorded_Returns404()
    {
        _mockRepository.GetTenantTimeZoneAsync(7).Returns(string.Empty);

        var result = await _controller.GetTimeZone(ValidApiKey, 7);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetTimeZone_RepositoryThrows_Returns500()
    {
        _mockRepository.GetTenantTimeZoneAsync(1).ThrowsAsync(new Exception("db blew up"));

        var result = await _controller.GetTimeZone(ValidApiKey, 1);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    // ------------------------------------------------------------------ shop -> tenant

    // The Deliver DFRNT Shopify app is one listing with one App URL, so install, the /api/Rates
    // carrier callback and every webhook arrive at a single shared Integration Manager deployment
    // serving all tenants. The shop domain is the only thing those requests carry that says which
    // Despatch database to open, and this is where that question is answered.
    [Fact]
    public async Task GetByShopifyShop_KnownShop_Returns200WithTenant()
    {
        _mockRepository.GetTenantByShopifyShopAsync("a-shop.myshopify.com")
            .Returns(new ShopifyShopTenantResponse { TenantId = 42, TenantCode = "URGENT", Shop = "a-shop.myshopify.com" });

        var result = await _controller.GetByShopifyShop(ValidApiKey, "a-shop.myshopify.com");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ShopifyShopTenantResponse>(okResult.Value);
        Assert.Equal(42, response.TenantId);
        Assert.Equal("URGENT", response.TenantCode);
    }

    [Fact]
    public async Task GetByShopifyShop_MissingApiKey_Returns401()
    {
        var result = await _controller.GetByShopifyShop(null, "a-shop.myshopify.com");

        Assert.IsType<UnauthorizedResult>(result);
        await _mockRepository.DidNotReceive().GetTenantByShopifyShopAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task GetByShopifyShop_WrongApiKey_Returns401()
    {
        var result = await _controller.GetByShopifyShop("wrong-key", "a-shop.myshopify.com");

        Assert.IsType<UnauthorizedResult>(result);
        await _mockRepository.DidNotReceive().GetTenantByShopifyShopAsync(Arg.Any<string>());
    }

    // A public app URL is asked about shops that never installed, or uninstalled long ago. That is
    // routine, not a fault - the caller turns it into a rejected request.
    [Fact]
    public async Task GetByShopifyShop_UnmappedShop_Returns404()
    {
        _mockRepository.GetTenantByShopifyShopAsync(Arg.Any<string>()).Returns((ShopifyShopTenantResponse?)null);

        var result = await _controller.GetByShopifyShop(ValidApiKey, "nobody.myshopify.com");

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetByShopifyShop_NoShop_Returns400(string? shop)
    {
        var result = await _controller.GetByShopifyShop(ValidApiKey, shop);

        Assert.IsType<BadRequestObjectResult>(result);
        await _mockRepository.DidNotReceive().GetTenantByShopifyShopAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task GetByShopifyShop_RepositoryThrows_Returns500()
    {
        _mockRepository.GetTenantByShopifyShopAsync(Arg.Any<string>()).ThrowsAsync(new Exception("db blew up"));

        var result = await _controller.GetByShopifyShop(ValidApiKey, "a-shop.myshopify.com");

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    // ------------------------------------------------------------------ recording a mapping

    [Fact]
    public async Task PutShopifyShop_NewShop_Returns204()
    {
        _mockRepository.MapShopifyShopAsync("a-shop.myshopify.com", 42)
            .Returns(ShopifyShopMappingResult.Mapped);

        var result = await _controller.PutShopifyShop(ValidApiKey, 42,
            new ShopifyShopTenantRequest { Shop = "a-shop.myshopify.com" });

        Assert.IsType<NoContentResult>(result);
    }

    // Install is not idempotent from Shopify's side - a merchant can reinstall - so re-recording
    // the same pairing has to succeed rather than look like a conflict.
    [Fact]
    public async Task PutShopifyShop_AlreadyMappedToTheSameTenant_Returns204()
    {
        _mockRepository.MapShopifyShopAsync("a-shop.myshopify.com", 42)
            .Returns(ShopifyShopMappingResult.AlreadyMapped);

        var result = await _controller.PutShopifyShop(ValidApiKey, 42,
            new ShopifyShopTenantRequest { Shop = "a-shop.myshopify.com" });

        Assert.IsType<NoContentResult>(result);
    }

    // The one that matters. Silently re-pointing a shop would move a live merchant's orders to a
    // different courier's database, so it is a conflict a human has to resolve.
    [Fact]
    public async Task PutShopifyShop_MappedToADifferentTenant_Returns409()
    {
        _mockRepository.MapShopifyShopAsync("a-shop.myshopify.com", 42)
            .Returns(ShopifyShopMappingResult.ConflictsWithAnotherTenant);

        var result = await _controller.PutShopifyShop(ValidApiKey, 42,
            new ShopifyShopTenantRequest { Shop = "a-shop.myshopify.com" });

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task PutShopifyShop_UnknownTenant_Returns404()
    {
        _mockRepository.MapShopifyShopAsync("a-shop.myshopify.com", 999)
            .Returns(ShopifyShopMappingResult.TenantNotFound);

        var result = await _controller.PutShopifyShop(ValidApiKey, 999,
            new ShopifyShopTenantRequest { Shop = "a-shop.myshopify.com" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PutShopifyShop_MissingApiKey_Returns401()
    {
        var result = await _controller.PutShopifyShop(null, 42,
            new ShopifyShopTenantRequest { Shop = "a-shop.myshopify.com" });

        Assert.IsType<UnauthorizedResult>(result);
        await _mockRepository.DidNotReceive().MapShopifyShopAsync(Arg.Any<string>(), Arg.Any<int>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PutShopifyShop_NoShop_Returns400(string? shop)
    {
        var result = await _controller.PutShopifyShop(ValidApiKey, 42,
            new ShopifyShopTenantRequest { Shop = shop! });

        Assert.IsType<BadRequestObjectResult>(result);
        await _mockRepository.DidNotReceive().MapShopifyShopAsync(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public async Task PutShopifyShop_RepositoryThrows_Returns500()
    {
        _mockRepository.MapShopifyShopAsync(Arg.Any<string>(), Arg.Any<int>()).ThrowsAsync(new Exception("db blew up"));

        var result = await _controller.PutShopifyShop(ValidApiKey, 42,
            new ShopifyShopTenantRequest { Shop = "a-shop.myshopify.com" });

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }
}
