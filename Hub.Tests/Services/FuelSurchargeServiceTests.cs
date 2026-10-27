using System.Security.Claims;
using Hub.Interfaces;
using Hub.Services;
using Hub.Tests.Helpers;
using Hub.ViewModels;
using NSubstitute;

namespace Hub.Tests.Services;

[Collection("EnvironmentVariables")]
public class FuelSurchargeServiceTests : IDisposable
{
    private readonly string _originalCredentials;

    public FuelSurchargeServiceTests()
    {
        _originalCredentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        Environment.SetEnvironmentVariable("SQLCredentials", ";User=test;Password=test;");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SQLCredentials", _originalCredentials);
        GC.SuppressFinalize(this);
    }

    private static readonly DateTime TenantNow = new(2026, 5, 11, 9, 0, 0, DateTimeKind.Unspecified);

    private static (FuelSurchargeService Service, ITenantService Tenants) CreateService(IFuelSurchargeRepository repo)
    {
        var tenantService = Substitute.For<ITenantService>();
        tenantService.GetCurrentTenantTimeAsync(Arg.Any<int>()).Returns(TenantNow);
        return (new FuelSurchargeService(new ConnectionStringManager(), repo, tenantService), tenantService);
    }

    private static ClaimsPrincipal UrgentUser(string clientId = "0", int currentTenantId = 1) =>
        ClaimsPrincipalFactory.Create(tenantCode: "urgent", clientId: clientId, currentTenantId: currentTenantId);

    private static async Task<FuelSurchargeCardViewModel> GetCardAsync(
        IFuelSurchargeRepository repo, ClaimsPrincipal? user = null)
    {
        var (service, _) = CreateService(repo);
        var result = await service.GetCardAsync(user ?? UrgentUser(), CancellationToken.None);
        Assert.Equal(FuelSurchargeStatus.Ok, result.Status);
        return Assert.IsType<FuelSurchargeCardViewModel>(result.Card);
    }

    private static IFuelSurchargeRepository MockRepo(List<FuelSurchargeRow> rows)
    {
        var repo = Substitute.For<IFuelSurchargeRepository>();
        repo.GetHistoryAsync(Arg.Any<int?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>(), Arg.Any<int>())
            .Returns(rows);
        return repo;
    }

    [Fact]
    public async Task GetCard_NoConnectionClaim_IsNotAuthenticated()
    {
        var (service, _) = CreateService(MockRepo([]));
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("TenantCode", "urgent")], "test"));

        var result = await service.GetCardAsync(user, CancellationToken.None);

        Assert.Equal(FuelSurchargeStatus.NotAuthenticated, result.Status);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task GetCard_Courier_IsNotAvailable()
    {
        var (service, _) = CreateService(MockRepo([]));
        var user = ClaimsPrincipalFactory.Create(tenantCode: "urgent", isCourier: true);

        var result = await service.GetCardAsync(user, CancellationToken.None);

        Assert.Equal(FuelSurchargeStatus.NotAvailable, result.Status);
    }

    [Fact]
    public async Task GetCard_NonUrgentTenant_IsNotAvailable()
    {
        var repo = MockRepo([]);
        var (service, _) = CreateService(repo);

        var result = await service.GetCardAsync(ClaimsPrincipalFactory.Create(tenantCode: "other"), CancellationToken.None);

        Assert.Equal(FuelSurchargeStatus.NotAvailable, result.Status);
        await repo.DidNotReceiveWithAnyArgs().GetHistoryAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetCard_PicksCurrentStandard_WhenEndDateIsInThePast()
    {
        // Production data often leaves End set to a past date with no successor
        // record; the most recently started standard rate should still surface.
        var rows = new List<FuelSurchargeRow>
        {
            new()
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 5.25m, PumpPrice = 2.4000m,
                Start = TenantNow.AddYears(-2), End = TenantNow.AddDays(-10),
                Active = true, IsCurrent = false
            }
        };

        var model = await GetCardAsync(MockRepo(rows));

        Assert.True(model.HasData);
        Assert.NotNull(model.CurrentStandard);
        Assert.Equal(5.25m, model.CurrentStandard!.Rate);
        Assert.Equal(2.4000m, model.PumpPrice);
    }

    [Fact]
    public async Task GetCard_PicksMostRecentStartedStandard_AsCurrent()
    {
        var rows = new List<FuelSurchargeRow>
        {
            new()
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 4.00m,
                Start = TenantNow.AddYears(-3), End = TenantNow.AddYears(-2), Active = true
            },
            new()
            {
                FuelSurchargeId = 2, ClientId = null, Rate = 6.50m, PumpPrice = 2.8000m,
                Start = TenantNow.AddMonths(-3), End = null, Active = true
            }
        };

        var model = await GetCardAsync(MockRepo(rows));

        Assert.NotNull(model.CurrentStandard);
        Assert.Equal(2, model.CurrentStandard!.FuelSurchargeId);
        Assert.Equal(2.8000m, model.PumpPrice);
    }

    [Fact]
    public async Task GetCard_IgnoresFutureDatedStandard()
    {
        var rows = new List<FuelSurchargeRow>
        {
            new() { FuelSurchargeId = 1, ClientId = null, Rate = 5.00m, Start = TenantNow.AddMonths(-1), Active = true },
            new() { FuelSurchargeId = 2, ClientId = null, Rate = 7.00m, Start = TenantNow.AddMonths(1), Active = true }
        };

        var model = await GetCardAsync(MockRepo(rows));

        Assert.NotNull(model.CurrentStandard);
        Assert.Equal(1, model.CurrentStandard!.FuelSurchargeId);
    }

    [Fact]
    public async Task GetCard_IgnoresInactiveStandard()
    {
        var rows = new List<FuelSurchargeRow>
        {
            new() { FuelSurchargeId = 1, ClientId = null, Rate = 5.00m, Start = TenantNow.AddMonths(-1), Active = false }
        };

        var model = await GetCardAsync(MockRepo(rows));

        Assert.Null(model.CurrentStandard);
    }

    [Fact]
    public async Task GetCard_ClientSpecificPumpPrice_TakesPrecedenceOverStandard()
    {
        var rows = new List<FuelSurchargeRow>
        {
            new()
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 5.00m, PumpPrice = 2.4000m,
                Start = TenantNow.AddMonths(-2), Active = true
            },
            new()
            {
                FuelSurchargeId = 2, ClientId = 42, Rate = 4.50m, PumpPrice = 2.6000m,
                Start = TenantNow.AddMonths(-1), Active = true
            }
        };

        var model = await GetCardAsync(MockRepo(rows), UrgentUser(clientId: "42"));

        Assert.Equal(2.6000m, model.PumpPrice);
    }

    [Fact]
    public async Task GetCard_UsesTenantTime_NotMachineLocalTime()
    {
        // Tenant "now" is 2026-05-11; the row starts a day before that and ends a day after -
        // a record that's only "current" by tenant clock.
        var rows = new List<FuelSurchargeRow>
        {
            new()
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 5.00m,
                Start = TenantNow.AddDays(-1), End = TenantNow.AddDays(1), Active = true
            },
            new() { FuelSurchargeId = 2, ClientId = null, Rate = 7.00m, Start = TenantNow.AddDays(2), Active = true }
        };
        var repo = MockRepo(rows);
        var (service, tenants) = CreateService(repo);

        var result = await service.GetCardAsync(UrgentUser(), CancellationToken.None);

        await tenants.Received(1).GetCurrentTenantTimeAsync(Arg.Any<int>());
        await repo.Received(1).GetHistoryAsync(Arg.Any<int?>(), TenantNow, Arg.Any<CancellationToken>(), Arg.Any<int>());
        Assert.Equal(1, result.Card!.CurrentStandard!.FuelSurchargeId);
    }

    [Fact]
    public async Task GetCard_SetsTodayToTenantTime_WhenRowsExist()
    {
        var rows = new List<FuelSurchargeRow>
        {
            new() { FuelSurchargeId = 1, ClientId = null, Rate = 5.00m, Start = TenantNow.AddDays(-1), Active = true }
        };

        var model = await GetCardAsync(MockRepo(rows));

        Assert.Equal(TenantNow, model.Today);
    }

    [Fact]
    public async Task GetCard_SetsTodayToTenantTime_WhenNoRows()
    {
        var model = await GetCardAsync(MockRepo([]));

        Assert.False(model.HasData);
        Assert.Equal(TenantNow, model.Today);
    }

    [Fact]
    public async Task GetCard_PassesParsedCurrentTenantId_ToTenantService()
    {
        var (service, tenants) = CreateService(MockRepo([]));

        await service.GetCardAsync(UrgentUser(currentTenantId: 7), CancellationToken.None);

        await tenants.Received(1).GetCurrentTenantTimeAsync(7);
    }

    [Fact]
    public async Task GetCard_PassesClientIdToRepository_NullWhenZero()
    {
        var repo = MockRepo([]);
        var (service, _) = CreateService(repo);

        await service.GetCardAsync(UrgentUser(clientId: "0"), CancellationToken.None);
        await service.GetCardAsync(UrgentUser(clientId: "42"), CancellationToken.None);

        await repo.Received(1).GetHistoryAsync(null, TenantNow, Arg.Any<CancellationToken>(), Arg.Any<int>());
        await repo.Received(1).GetHistoryAsync(42, TenantNow, Arg.Any<CancellationToken>(), Arg.Any<int>());
    }

    [Fact]
    public async Task GetCard_MarksOnlyOneStandardRow_AsCurrent_InHistory()
    {
        var rows = new List<FuelSurchargeRow>
        {
            new()
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 4.00m,
                Start = TenantNow.AddYears(-2), End = TenantNow.AddYears(-1), Active = true
            },
            new()
            {
                FuelSurchargeId = 2, ClientId = null, Rate = 5.00m,
                Start = TenantNow.AddMonths(-6), End = TenantNow.AddMonths(-3), Active = true
            },
            new()
            {
                FuelSurchargeId = 3, ClientId = null, Rate = 6.00m,
                Start = TenantNow.AddMonths(-1), End = null, Active = true
            }
        };

        var model = await GetCardAsync(MockRepo(rows));

        var currents = model.History.Where(r => r.IsCurrent).ToList();
        Assert.Single(currents);
        Assert.Equal(3, currents[0].FuelSurchargeId);
    }
}
