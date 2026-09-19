using Hub.Controllers;
using Hub.Interfaces;
using Hub.Tests.Helpers;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Controllers;

[Collection("EnvironmentVariables")]
public class FuelSurchargeControllerTests : IDisposable
{
    private readonly string _originalCredentials;

    public FuelSurchargeControllerTests()
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

    private static FuelSurchargeController CreateController(
        IFuelSurchargeRepository repo,
        System.Security.Claims.ClaimsPrincipal? user = null)
    {
        user ??= ClaimsPrincipalFactory.Create(tenantCode: "urgent", clientId: "0");
        var connectionStringManager = new ConnectionStringManager();
        var tenantService = Substitute.For<ITenantService>();
        tenantService.GetCurrentTenantTimeAsync(Arg.Any<int>()).Returns(TenantNow);
        var controller = new FuelSurchargeController(connectionStringManager, repo, tenantService);
        ControllerTestBase.SetupHttpContext(controller, user);
        return controller;
    }

    private static FuelSurchargeCardViewModel GetModel(IActionResult result)
    {
        var view = Assert.IsType<ViewResult>(result);
        return Assert.IsType<FuelSurchargeCardViewModel>(view.Model);
    }

    private static IFuelSurchargeRepository MockRepo(List<FuelSurchargeRow> rows)
    {
        var repo = Substitute.For<IFuelSurchargeRepository>();
        repo.GetHistoryAsync(Arg.Any<int?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>(), Arg.Any<int>())
            .Returns(rows);
        return repo;
    }

    [Fact]
    public async Task Index_PicksCurrentStandard_WhenEndDateIsInThePast()
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
        var controller = CreateController(MockRepo(rows));

        var model = GetModel(await controller.Index(CancellationToken.None));

        Assert.True(model.HasData);
        Assert.NotNull(model.CurrentStandard);
        Assert.Equal(5.25m, model.CurrentStandard!.Rate);
        Assert.Equal(2.4000m, model.PumpPrice);
    }

    [Fact]
    public async Task Index_PicksMostRecentStartedStandard_AsCurrent()
    {
        var rows = new List<FuelSurchargeRow>
        {
            new()
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 4.00m,
                Start = TenantNow.AddYears(-3), End = TenantNow.AddYears(-2),
                Active = true
            },
            new()
            {
                FuelSurchargeId = 2, ClientId = null, Rate = 6.50m, PumpPrice = 2.8000m,
                Start = TenantNow.AddMonths(-3), End = null,
                Active = true
            }
        };
        var controller = CreateController(MockRepo(rows));

        var model = GetModel(await controller.Index(CancellationToken.None));

        Assert.NotNull(model.CurrentStandard);
        Assert.Equal(2, model.CurrentStandard!.FuelSurchargeId);
        Assert.Equal(2.8000m, model.PumpPrice);
    }

    [Fact]
    public async Task Index_IgnoresFutureDatedStandard()
    {
        var rows = new List<FuelSurchargeRow>
        {
            new()
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 5.00m,
                Start = TenantNow.AddMonths(-1), End = null,
                Active = true
            },
            new()
            {
                FuelSurchargeId = 2, ClientId = null, Rate = 7.00m,
                Start = TenantNow.AddMonths(1), End = null,
                Active = true
            }
        };
        var controller = CreateController(MockRepo(rows));

        var model = GetModel(await controller.Index(CancellationToken.None));

        Assert.NotNull(model.CurrentStandard);
        Assert.Equal(1, model.CurrentStandard!.FuelSurchargeId);
    }

    [Fact]
    public async Task Index_IgnoresInactiveStandard()
    {
        var rows = new List<FuelSurchargeRow>
        {
            new()
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 5.00m,
                Start = TenantNow.AddMonths(-1), End = null,
                Active = false
            }
        };
        var controller = CreateController(MockRepo(rows));

        var model = GetModel(await controller.Index(CancellationToken.None));

        Assert.Null(model.CurrentStandard);
    }

    [Fact]
    public async Task Index_ClientSpecificPumpPrice_TakesPrecedenceOverStandard()
    {
        var rows = new List<FuelSurchargeRow>
        {
            new()
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 5.00m, PumpPrice = 2.4000m,
                Start = TenantNow.AddMonths(-2), End = null,
                Active = true
            },
            new()
            {
                FuelSurchargeId = 2, ClientId = 42, Rate = 4.50m, PumpPrice = 2.6000m,
                Start = TenantNow.AddMonths(-1), End = null,
                Active = true
            }
        };
        var user = ClaimsPrincipalFactory.Create(tenantCode: "urgent", clientId: "42");
        var controller = CreateController(MockRepo(rows), user);

        var model = GetModel(await controller.Index(CancellationToken.None));

        Assert.Equal(2.6000m, model.PumpPrice);
    }

    [Fact]
    public async Task Index_UsesTenantTime_NotMachineLocalTime()
    {
        // Stub tenant time to a fixed moment far from DateTime.Now so we can
        // verify "current" picks rows relative to tenant time, not server time.
        // Tenant "now" is 2026-05-11; the row starts a day before that and
        // ends a day after — a record that's only "current" by tenant clock.
        var rows = new List<FuelSurchargeRow>
        {
            new()
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 5.00m,
                Start = TenantNow.AddDays(-1), End = TenantNow.AddDays(1),
                Active = true
            },
            new()
            {
                FuelSurchargeId = 2, ClientId = null, Rate = 7.00m,
                Start = TenantNow.AddDays(2), End = null,
                Active = true
            }
        };
        var repo = MockRepo(rows);
        var user = ClaimsPrincipalFactory.Create(tenantCode: "urgent", clientId: "0");
        var connectionStringManager = new ConnectionStringManager();
        var tenantService = Substitute.For<ITenantService>();
        tenantService.GetCurrentTenantTimeAsync(Arg.Any<int>()).Returns(TenantNow);
        var controller = new FuelSurchargeController(connectionStringManager, repo, tenantService);
        ControllerTestBase.SetupHttpContext(controller, user);

        var model = GetModel(await controller.Index(CancellationToken.None));

        await tenantService.Received(1).GetCurrentTenantTimeAsync(Arg.Any<int>());
        await repo.Received(1).GetHistoryAsync(Arg.Any<int?>(), TenantNow, Arg.Any<CancellationToken>(), Arg.Any<int>());
        Assert.NotNull(model.CurrentStandard);
        Assert.Equal(1, model.CurrentStandard!.FuelSurchargeId);
    }

    [Fact]
    public async Task Index_PassesParsedCurrentTenantId_ToTenantService()
    {
        var repo = MockRepo([]);
        var user = ClaimsPrincipalFactory.Create(tenantCode: "urgent", clientId: "0", currentTenantId: 7);
        var connectionStringManager = new ConnectionStringManager();
        var tenantService = Substitute.For<ITenantService>();
        tenantService.GetCurrentTenantTimeAsync(Arg.Any<int>()).Returns(TenantNow);
        var controller = new FuelSurchargeController(connectionStringManager, repo, tenantService);
        ControllerTestBase.SetupHttpContext(controller, user);

        await controller.Index(CancellationToken.None);

        await tenantService.Received(1).GetCurrentTenantTimeAsync(7);
    }

    [Fact]
    public async Task Index_MarksOnlyOneStandardRow_AsCurrent_InHistory()
    {
        var rows = new List<FuelSurchargeRow>
        {
            new()
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 4.00m,
                Start = TenantNow.AddYears(-2), End = TenantNow.AddYears(-1),
                Active = true
            },
            new()
            {
                FuelSurchargeId = 2, ClientId = null, Rate = 5.00m,
                Start = TenantNow.AddMonths(-6), End = TenantNow.AddMonths(-3),
                Active = true
            },
            new()
            {
                FuelSurchargeId = 3, ClientId = null, Rate = 6.00m,
                Start = TenantNow.AddMonths(-1), End = null,
                Active = true
            }
        };
        var controller = CreateController(MockRepo(rows));

        var model = GetModel(await controller.Index(CancellationToken.None));

        var currents = model.History.Where(r => r.IsCurrent).ToList();
        Assert.Single(currents);
        Assert.Equal(3, currents[0].FuelSurchargeId);
    }
}
