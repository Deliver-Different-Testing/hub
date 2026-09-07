using System.Security.Cryptography;
using Hub.Controllers;
using Hub.Interfaces;
using Hub.Models.Master;
using Hub.Services;
using Hub.Shared;
using Hub.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Controllers;

/// <summary>
/// Shopify merchant sign-in: the one screen the old Urgent app asked a merchant for after install,
/// now doing the extra thing a single-tenant app never had to - working out which courier the
/// merchant belongs to.
/// <para>
/// This is a password endpoint reachable from the public internet by proxy, so most of what follows
/// is about what it refuses and what it declines to say.
/// </para>
/// </summary>
[Collection("PartnerDirectoryApiKey")]
public class ShopifySignInControllerTests : IDisposable
{
    private const string SetupKey = "setup-key-123";
    private const string Shop = "a-shop.myshopify.com";
    private const string Email = "merchant@test.com";
    private const string Password = "Test@1234";
    private const string Salt = "abcde";

    private readonly IAuthenticationRepository _repository = Substitute.For<IAuthenticationRepository>();
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ShopifyLinkTicketKey _ticketKey;
    private readonly ShopifySignInController _controller;
    private readonly IShopifyLinkTicketIssuer _tickets;

    public ShopifySignInControllerTests()
    {
        _ticketKey = new ShopifyLinkTicketKey(_key.ExportPkcs8PrivateKeyPem(), "test-key-1");
        _tickets = new ShopifyLinkTicketIssuer(_ticketKey);
        _controller = new ShopifySignInController(_repository, _tickets);
        Environment.SetEnvironmentVariable("ShopifySetupApiKey", SetupKey);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable("ShopifySetupApiKey", null);
        _ticketKey.Dispose();
        _key.Dispose();
    }

    private static User AMerchant(bool legacy = false) => new()
    {
        UserId = 7,
        Email = Email,
        Salt = Salt,
        Password = legacy
            ? PasswordHelper.HashPasswordLegacy(Password, Salt)
            : PasswordHelper.HashPassword(Password, Salt),
        IsLegacyHash = legacy,
        IsCourier = false
    };

    private static ShopifySignInTenant ACourier(int tenantId, string? host = null) => new()
    {
        TenantId = tenantId,
        Code = $"courier{tenantId}",
        Name = $"Courier {tenantId}",
        Host = host ?? $"https://tenant{tenantId}.example.com"
    };

    private void KnownMerchant(bool legacy = false) =>
        _repository.GetShopifyMerchantUserAsync(Email).Returns(AMerchant(legacy));

    private void Couriers(params ShopifySignInTenant[] tenants) =>
        _repository.GetShopifyTenantsForUserAsync(7).Returns(tenants);

    private Task<IActionResult> SignIn(string? password = Password, string? username = Email,
        string? shop = Shop, string? apiKey = SetupKey) =>
        _controller.SignIn(apiKey, shop, new ShopifySignInRequest { Username = username, Password = password });

    private static ShopifySignInResponse Body(IActionResult result) =>
        Assert.IsType<ShopifySignInResponse>(Assert.IsType<OkObjectResult>(result).Value);

    // ------------------------------------------------------------------ the door

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    public async Task SignIn_WithoutTheSetupKey_Is401AndReadsNothing(string? apiKey)
    {
        Assert.IsType<UnauthorizedResult>(await SignIn(apiKey: apiKey));

        await _repository.DidNotReceive().GetShopifyMerchantUserAsync(Arg.Any<string>());
    }

    /// <summary>
    /// The shop rides in the query rather than only the body because the rate limiter partitions on
    /// it, and a partitioner only ever sees the HttpContext - never a deserialised body.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SignIn_WithNoShop_Is400(string? shop)
    {
        Assert.IsType<BadRequestObjectResult>(await SignIn(shop: shop));
    }

    [Theory]
    [InlineData(null, Password)]
    [InlineData("", Password)]
    [InlineData(Email, null)]
    [InlineData(Email, "")]
    public async Task SignIn_WithHalfACredential_Is400(string? username, string? password)
    {
        Assert.IsType<BadRequestObjectResult>(await SignIn(password, username));
    }

    // ------------------------------------------------------------------ the credential

    /// <summary>
    /// Unknown user and wrong password are the same answer, in the same words. Telling them apart
    /// turns this into a way to enumerate every dispatch account in the estate.
    /// </summary>
    [Fact]
    public async Task SignIn_WithAnUnknownUser_Is401()
    {
        _repository.GetShopifyMerchantUserAsync(Email).Returns((User?)null);

        Assert.IsType<UnauthorizedObjectResult>(await SignIn());
    }

    [Fact]
    public async Task SignIn_WithTheWrongPassword_Is401()
    {
        KnownMerchant();

        Assert.IsType<UnauthorizedObjectResult>(await SignIn(password: "Test@12345"));
    }

    [Fact]
    public async Task SignIn_WithAnUnknownUserAndAWrongPassword_SayTheSameThing()
    {
        KnownMerchant();
        var wrongPassword = Assert.IsType<UnauthorizedObjectResult>(await SignIn(password: "nope"));

        _repository.GetShopifyMerchantUserAsync("nobody@test.com").Returns((User?)null);
        var unknownUser = Assert.IsType<UnauthorizedObjectResult>(await SignIn(username: "nobody@test.com"));

        Assert.Equivalent(wrongPassword.Value, unknownUser.Value);
    }

    /// <summary>
    /// A legacy row is PBKDF2-SHA1 at 1,000 iterations against SHA256 at 10,000 - ten times cheaper
    /// to attack offline. Upgrading on the way through is the only moment we ever hold the plaintext.
    /// </summary>
    [Fact]
    public async Task SignIn_WithALegacyHash_AcceptsItAndUpgradesItInPlace()
    {
        var user = AMerchant(legacy: true);
        _repository.GetShopifyMerchantUserAsync(Email).Returns(user);
        Couriers(ACourier(42));

        Assert.IsType<OkObjectResult>(await SignIn());

        Assert.False(user.IsLegacyHash);
        Assert.Equal(PasswordHelper.HashPassword(Password, Salt), user.Password);
        await _repository.Received(1).SaveAsync();
    }

    [Fact]
    public async Task SignIn_WithAModernHash_WritesNothing()
    {
        KnownMerchant();
        Couriers(ACourier(42));

        await SignIn();

        await _repository.DidNotReceive().SaveAsync();
    }

    /// <summary>
    /// Signing into a Shopify store must not move the user's portal tenant out from under a Hub
    /// session they have open in another tab.
    /// </summary>
    [Fact]
    public async Task SignIn_NeverMovesTheUsersCurrentTenant()
    {
        KnownMerchant();
        Couriers(ACourier(42));

        await SignIn();

        await _repository.DidNotReceive().UpdateCurrentTenantIdAsync(Arg.Any<int>(), Arg.Any<int>());
    }

    // ------------------------------------------------------------------ which courier

    [Fact]
    public async Task SignIn_WithOneCourier_ReturnsItAndALinkTicket()
    {
        KnownMerchant();
        Couriers(ACourier(42));

        var body = Body(await SignIn());

        Assert.Equal(42, body.Tenant?.TenantId);
        Assert.NotNull(body.LinkTicket);
        Assert.Null(body.SelectionTicket);
    }

    /// <summary>
    /// Nothing is committed here in any case - the mapping is the front door's write - but a merchant
    /// with a choice to make must not be handed a ticket that authorises one of the options.
    /// </summary>
    [Fact]
    public async Task SignIn_WithSeveralCouriers_ReturnsTheListAndNoLinkTicket()
    {
        KnownMerchant();
        Couriers(ACourier(42), ACourier(43));

        var body = Body(await SignIn());

        Assert.Equal([42, 43], body.Tenants.Select(t => t.TenantId));
        Assert.Null(body.Tenant);
        Assert.Null(body.LinkTicket);
        Assert.NotNull(body.SelectionTicket);
    }

    /// <summary>
    /// A correct password with no courier behind it is still a correct password, so this is a 200.
    /// A distinct status here would be the enumeration oracle the 401 wording avoids.
    /// </summary>
    [Fact]
    public async Task SignIn_WithNoCourierSwitchedOn_Is200WithNothingToOffer()
    {
        KnownMerchant();
        Couriers();

        var body = Body(await SignIn());

        Assert.Empty(body.Tenants);
        Assert.Null(body.LinkTicket);
        Assert.Null(body.SelectionTicket);
    }

    /// <summary>
    /// The same sanity check the front door applies to a host before routing to it. A malformed row
    /// must not become a courier the merchant picks and then fails on.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/only")]
    public async Task SignIn_DropsACourierWhoseHostIsUnusable(string host)
    {
        KnownMerchant();
        Couriers(ACourier(42, host));

        Assert.Empty(Body(await SignIn()).Tenants);
    }

    [Fact]
    public async Task SignIn_TrimsATrailingSlashFromAHost()
    {
        KnownMerchant();
        Couriers(ACourier(42, "https://tenant42.example.com/"));

        Assert.Equal("https://tenant42.example.com", Body(await SignIn()).Tenant?.Host);
    }

    // ------------------------------------------------------------------ choosing a courier

    private async Task<string> SelectionTicketFor(params ShopifySignInTenant[] couriers)
    {
        KnownMerchant();
        Couriers(couriers);
        return Body(await SignIn()).SelectionTicket!;
    }

    private Task<IActionResult> Select(string? ticket, int tenantId, string? shop = Shop,
        string? apiKey = SetupKey) =>
        _controller.SelectTenant(apiKey, shop,
            new ShopifySelectTenantRequest { SelectionTicket = ticket, TenantId = tenantId });

    [Fact]
    public async Task SelectTenant_WithACourierTheMerchantBelongsTo_ReturnsALinkTicket()
    {
        var ticket = await SelectionTicketFor(ACourier(42), ACourier(43));
        _repository.IsUserAssociatedWithTenantAsync(7, 43).Returns(true);

        var body = Body(await Select(ticket, 43));

        Assert.Equal(43, body.Tenant?.TenantId);
        Assert.NotNull(body.LinkTicket);
    }

    /// <summary>
    /// Membership is re-read from master rather than taken from the ticket. The ticket's candidate
    /// list is a convenience for the screen; this is the gate.
    /// </summary>
    [Fact]
    public async Task SelectTenant_ReChecksMembershipAgainstMasterRatherThanTheTicket()
    {
        var ticket = await SelectionTicketFor(ACourier(42), ACourier(43));
        _repository.IsUserAssociatedWithTenantAsync(7, 43).Returns(false);

        Assert.IsType<ObjectResult>(await Select(ticket, 43));
        Assert.Equal(StatusCodes.Status403Forbidden,
            ((ObjectResult)await Select(ticket, 43)).StatusCode);
    }

    [Fact]
    public async Task SelectTenant_WithACourierThatWasNeverOffered_IsRefused()
    {
        var ticket = await SelectionTicketFor(ACourier(42), ACourier(43));
        _repository.IsUserAssociatedWithTenantAsync(7, 99).Returns(true);

        Assert.Equal(StatusCodes.Status403Forbidden,
            ((ObjectResult)await Select(ticket, 99)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-ticket")]
    public async Task SelectTenant_WithoutAUsableTicket_Is401(string? ticket)
    {
        Assert.IsType<UnauthorizedObjectResult>(await Select(ticket, 42));
    }

    /// <summary>
    /// The ticket names the store it was issued for. Letting a different one through would make the
    /// picker a way to attach someone else's shop to a courier this merchant belongs to.
    /// </summary>
    [Fact]
    public async Task SelectTenant_ForADifferentShopThanTheTicketNames_Is401()
    {
        var ticket = await SelectionTicketFor(ACourier(42), ACourier(43));
        _repository.IsUserAssociatedWithTenantAsync(7, 43).Returns(true);

        Assert.IsType<UnauthorizedObjectResult>(await Select(ticket, 43, shop: "other.myshopify.com"));
    }

    [Fact]
    public async Task SelectTenant_WithoutTheSetupKey_Is401()
    {
        var ticket = await SelectionTicketFor(ACourier(42), ACourier(43));

        Assert.IsType<UnauthorizedResult>(await Select(ticket, 43, apiKey: "wrong"));
    }
}
