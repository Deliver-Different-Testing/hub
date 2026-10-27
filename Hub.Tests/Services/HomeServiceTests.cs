using System.Security.Claims;
using Hub.Interfaces;
using Hub.Models;
using Hub.Repositories;
using Hub.Services;
using Hub.Tests.Helpers;
using Hub.ViewModels;
using NSubstitute;

namespace Hub.Tests.Services;

[Collection("EnvironmentVariables")]
public class HomeServiceTests : IDisposable
{
    private readonly string _originalCredentials;
    private readonly string _originalTenantUrl;

    public HomeServiceTests()
    {
        _originalCredentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        _originalTenantUrl = Environment.GetEnvironmentVariable("TenantURL") ?? string.Empty;
        Environment.SetEnvironmentVariable("SQLCredentials", ";User=test;Password=test;");
        Environment.SetEnvironmentVariable("TenantURL", "https://app_name.example.com");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SQLCredentials", _originalCredentials);
        Environment.SetEnvironmentVariable("TenantURL", _originalTenantUrl);
        GC.SuppressFinalize(this);
    }

    private static HomeService CreateService(
        ITenantService? tenantService = null,
        IFeatureResolver? featureResolver = null,
        ITileAccessResolver? tileAccessResolver = null)
    {
        var context = TestDespatchContextFactory.CreateWithSeedData();
        var connectionStringManager = new ConnectionStringManager();
        tenantService ??= Substitute.For<ITenantService>();
        var repo = new Repository(context, tenantService);

        // Mock the stored procedures
        var mockProcs = Substitute.For<IDespatchContextProcedures>();
        mockProcs.RVW_stpValidateInternetPermissionsAsync(
                Arg.Any<int?>(), Arg.Any<OutputParameter<int>>(), Arg.Any<CancellationToken>())
            .Returns([
                new RVW_stpValidateInternetPermissionsResult { InternetPermissionID = 2, ClientContactID = 1 },
                new RVW_stpValidateInternetPermissionsResult { InternetPermissionID = 12, ClientContactID = 1 },
                new RVW_stpValidateInternetPermissionsResult { InternetPermissionID = 11, ClientContactID = 1 }
            ]);
        context.Procedures = mockProcs;

        // MATCH ALL THREE ARGUMENTS EXPLICITLY. Configuring this with the clientId alone let C# fill
        // in the optional parameters, so the substitute was only ever set up for (any, false, null).
        // Adding the countryCode parameter broke that accident: the call no longer matched, and
        // NSubstitute returned null - which surfaces as an unrelated-looking ArgumentNullException on
        // 'visibleFeatures' inside HubTileCatalogue. Arg.Any on every parameter says what these tests
        // actually mean, which is that gate 1 is not what they are covering.
        if (featureResolver is null)
        {
            featureResolver = Substitute.For<IFeatureResolver>();
            featureResolver.ResolveForClientAsync(Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<string?>())
                .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        // Gate 2 (tile access) is substituted to "no decisions", i.e. it removes nothing from the
        // visible set. These tests cover the service wiring, not the tile rules - those live in
        // TileAccessResolverTests.
        if (tileAccessResolver is null)
        {
            tileAccessResolver = Substitute.For<ITileAccessResolver>();
            tileAccessResolver.ResolveAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<ISet<string>>())
                .Returns(new List<TileAccess>());
        }

        return new HomeService(connectionStringManager, repo, featureResolver, tileAccessResolver, tenantService);
    }

    private static ClaimsPrincipal Staff() => ClaimsPrincipalFactory.Create();

    [Fact]
    public async Task GetHome_NotAuthenticated_ReturnsNull()
    {
        var service = CreateService();

        Assert.Null(await service.GetHomeAsync(ClaimsPrincipalFactory.CreateAnonymous()));
    }

    [Fact]
    public async Task GetHome_Authenticated_ReturnsResponse()
    {
        var service = CreateService();

        var home = await service.GetHomeAsync(Staff());

        Assert.NotNull(home);
        Assert.Equal("staff@test.com", home.UserEmail);
        Assert.False(home.IsCourier);
    }

    [Fact]
    public async Task GetHome_CourierUser_ChecksAfterHours()
    {
        var courier = ClaimsPrincipalFactory.Create(
            email: "courier@test.com", isCourier: true, courierId: 1, contactId: "1",
            timeZone: "New Zealand Standard Time");
        var service = CreateService();

        var home = await service.GetHomeAsync(courier);

        // Courier 1 has after-hours record in seed data
        Assert.NotNull(home);
        Assert.True(home.IsCourier);
        Assert.True(home.ShowAfterHours);
    }

    [Fact]
    public async Task GetHome_CourierUser_GetsTheHardcodedCourierTiles()
    {
        var courier = ClaimsPrincipalFactory.Create(isCourier: true, courierId: 1);
        var service = CreateService();

        var home = await service.GetHomeAsync(courier);

        Assert.NotNull(home);
        Assert.Equal(
            ["After Hours", "Courier Portal", "DFRNT Drive"],
            home.Tiles.Select(t => t.Title).ToArray());
        Assert.Equal("https://afterhours.example.com", home.Tiles[0].Href);
        Assert.Equal("https://courierportal.example.com", home.Tiles[1].Href);
        Assert.Equal("https://dfrntdriveconfig.example.com", home.Tiles[2].Href);
        Assert.Equal("/images/icons/dispatch.svg", home.Tiles[0].Icon);
        Assert.All(home.Tiles, t => Assert.False(t.IsDisabled));
    }

    [Fact]
    public async Task GetHome_CourierWithoutAfterHours_OmitsTheAfterHoursTile()
    {
        var courier = ClaimsPrincipalFactory.Create(isCourier: true, courierId: 999);
        var service = CreateService();

        var home = await service.GetHomeAsync(courier);

        Assert.NotNull(home);
        Assert.False(home.ShowAfterHours);
        Assert.Equal(["Courier Portal", "DFRNT Drive"], home.Tiles.Select(t => t.Title).ToArray());
    }

    [Fact]
    public async Task GetHome_NonCourierUser_ShowAfterHoursFalse()
    {
        var service = CreateService();

        var home = await service.GetHomeAsync(ClaimsPrincipalFactory.Create(isCourier: false));

        Assert.NotNull(home);
        Assert.False(home.ShowAfterHours);
    }

    [Fact]
    public async Task GetHome_ReturnsTenantCode()
    {
        var service = CreateService();

        var home = await service.GetHomeAsync(Staff());

        Assert.Equal("test", home!.TenantCode);
    }

    [Fact]
    public async Task GetHome_SetsTodayToTenantTime_NotServerTime()
    {
        var tenantNow = new DateTime(2026, 5, 11, 9, 0, 0, DateTimeKind.Unspecified);
        var tenantService = Substitute.For<ITenantService>();
        tenantService.GetCurrentTenantTimeAsync(Arg.Any<int>()).Returns(tenantNow);
        var service = CreateService(tenantService);

        var home = await service.GetHomeAsync(Staff());

        Assert.Equal(tenantNow, home!.Today);
    }

    [Fact]
    public async Task GetHome_PassesCurrentTenantIdClaim_ToTenantService()
    {
        var tenantService = Substitute.For<ITenantService>();
        var service = CreateService(tenantService);

        await service.GetHomeAsync(ClaimsPrincipalFactory.Create(currentTenantId: 7));

        await tenantService.Received(1).GetCurrentTenantTimeAsync(7);
    }

    [Fact]
    public async Task GetHome_ResolvesTilesFromTheCatalogue_WithAbsoluteIconPaths()
    {
        var featureResolver = Substitute.For<IFeatureResolver>();
        featureResolver.ResolveForClientAsync(Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<string?>())
            .Returns(new HashSet<string>(["hub-tile-accounts", "hub-tile-booking"], StringComparer.OrdinalIgnoreCase));
        var service = CreateService(featureResolver: featureResolver);

        var home = await service.GetHomeAsync(Staff());

        Assert.NotNull(home);
        var accounts = Assert.Single(home.Tiles, t => t.Key == "hub-tile-accounts");
        Assert.Equal("Accounts", accounts.Title);
        Assert.Equal("/images/icons/accounts.svg", accounts.Icon);
        Assert.Equal("https://accounts.example.com", accounts.Href);
        Assert.False(accounts.IsDisabled);
        var booking = Assert.Single(home.Tiles, t => t.Key == "hub-tile-booking");
        Assert.Equal("https://booking.example.com/#/login/", booking.Href);
    }

    [Fact]
    public async Task GetHome_TileDeniedByTileAccess_IsRemoved()
    {
        var featureResolver = Substitute.For<IFeatureResolver>();
        featureResolver.ResolveForClientAsync(Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<string?>())
            .Returns(new HashSet<string>(["hub-tile-accounts", "hub-tile-booking"], StringComparer.OrdinalIgnoreCase));
        var tileAccess = Substitute.For<ITileAccessResolver>();
        tileAccess.ResolveAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<ISet<string>>())
            .Returns([new TileAccess("hub-tile-accounts", false, TileAccessReason.RoleLacksTile)]);
        var service = CreateService(featureResolver: featureResolver, tileAccessResolver: tileAccess);

        var home = await service.GetHomeAsync(Staff());

        Assert.DoesNotContain(home!.Tiles, t => t.Key == "hub-tile-accounts");
        Assert.Contains(home.Tiles, t => t.Key == "hub-tile-booking");
    }

    [Fact]
    public async Task GetHome_FuelSurchargeTile_OnlyOnUrgentTenantForStaff()
    {
        var featureResolver = Substitute.For<IFeatureResolver>();
        featureResolver.ResolveForClientAsync(Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<string?>())
            .Returns(new HashSet<string>(["hub-tile-fuel-surcharge"], StringComparer.OrdinalIgnoreCase));
        var service = CreateService(featureResolver: featureResolver);

        var urgent = await service.GetHomeAsync(ClaimsPrincipalFactory.Create(tenantCode: "urgent"));
        var other = await service.GetHomeAsync(ClaimsPrincipalFactory.Create(tenantCode: "test"));

        var tile = Assert.Single(urgent!.Tiles);
        Assert.Equal("/FuelSurcharge", tile.Href);
        Assert.Equal("/images/icons/fuel%20icon.svg", tile.Icon);
        Assert.Empty(other!.Tiles);
    }
}
