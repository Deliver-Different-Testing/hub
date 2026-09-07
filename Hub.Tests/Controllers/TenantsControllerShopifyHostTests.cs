using Hub.Controllers;
using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Controllers;

/// <summary>
/// Registering where a courier's Integration Manager lives.
/// <para>
/// The row's existence is what "Shopify is switched on for this courier" means, and until this
/// endpoint existed nothing wrote it - three separate comments across two repos said Hub did, and
/// none of them was true, so the table was empty and every merchant would have been told no courier
/// was available. It used to be a side effect of issuing a pairing code; that call is going away, so
/// it becomes a thing a deployment does for itself when it starts.
/// </para>
/// </summary>
[Collection("PartnerDirectoryApiKey")]
public class TenantsControllerShopifyHostTests : IDisposable
{
    private const string TrustedKey = "trusted-key-123";
    private const string SetupKey = "setup-key-456";
    private const string Url = "https://tenant42.example.com";

    private readonly IAuthenticationRepository _repository = Substitute.For<IAuthenticationRepository>();
    private readonly TenantsController _controller;

    public TenantsControllerShopifyHostTests()
    {
        _controller = new TenantsController(_repository);
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", TrustedKey);
        Environment.SetEnvironmentVariable("ShopifySetupApiKey", SetupKey);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", null);
        Environment.SetEnvironmentVariable("ShopifySetupApiKey", null);
    }

    private Task<IActionResult> Put(string? url, int tenantId = 42, string? apiKey = TrustedKey) =>
        _controller.PutShopifyHost(apiKey, tenantId,
            new ShopifyTenantHostRequest { IntegrationManagerUrl = url });

    [Fact]
    public async Task PutShopifyHost_RecordsWhereTheCouriersIntegrationManagerLives()
    {
        _repository.UpsertShopifyTenantHostAsync(42, Url).Returns(true);

        Assert.IsType<NoContentResult>(await Put(Url));

        await _repository.Received(1).UpsertShopifyTenantHostAsync(42, Url);
    }

    [Fact]
    public async Task PutShopifyHost_ForATenantThatDoesNotExist_Is404()
    {
        _repository.UpsertShopifyTenantHostAsync(42, Url).Returns(false);

        Assert.IsType<NotFoundResult>(await Put(Url));
    }

    /// <summary>
    /// Trusted only. The front door reads this table to route a merchant; being able to write it
    /// would let the most exposed service in the estate point a courier's merchants at a host of its
    /// choosing.
    /// </summary>
    [Fact]
    public async Task PutShopifyHost_WithTheSetupServiceKey_IsRefused()
    {
        Assert.IsType<UnauthorizedResult>(await Put(Url, apiKey: SetupKey));

        await _repository.DidNotReceive().UpsertShopifyTenantHostAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PutShopifyHost_WithNoUrl_Is400(string? url)
    {
        Assert.IsType<BadRequestObjectResult>(await Put(url));

        await _repository.DidNotReceive().UpsertShopifyTenantHostAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    /// <summary>
    /// Refused here rather than filtered out at read time. A host that cannot be routed to is a
    /// courier switched on in name only, and the deployment registering it is the one place that can
    /// still be told it got it wrong.
    /// </summary>
    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/only")]
    [InlineData("tenant42.example.com")]
    public async Task PutShopifyHost_WithAUrlThatCannotBeRoutedTo_Is400(string url)
    {
        Assert.IsType<BadRequestObjectResult>(await Put(url));

        await _repository.DidNotReceive().UpsertShopifyTenantHostAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    /// <summary>
    /// Stored without it, so the front door and Hub agree on the string without either having to
    /// normalise the other's.
    /// </summary>
    [Fact]
    public async Task PutShopifyHost_StoresTheUrlWithoutATrailingSlash()
    {
        _repository.UpsertShopifyTenantHostAsync(42, Url).Returns(true);

        await Put($"{Url}/");

        await _repository.Received(1).UpsertShopifyTenantHostAsync(42, Url);
    }
}
