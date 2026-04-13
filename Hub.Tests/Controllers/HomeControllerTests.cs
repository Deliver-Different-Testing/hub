using Hub.Controllers;
using Hub.Interfaces;
using Hub.Models;
using Hub.Repositories;
using Hub.Tests.Helpers;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Controllers;

[Collection("EnvironmentVariables")]
public class HomeControllerTests : IDisposable
{
    private readonly string _originalCredentials;

    public HomeControllerTests()
    {
        _originalCredentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        Environment.SetEnvironmentVariable("SQLCredentials", ";User=test;Password=test;");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SQLCredentials", _originalCredentials);
        GC.SuppressFinalize(this);
    }

    private static HomeController CreateController(
        System.Security.Claims.ClaimsPrincipal? user = null)
    {
        var context = TestDespatchContextFactory.CreateWithSeedData();
        var connectionStringManager = new ConnectionStringManager();
        var tenantService = Substitute.For<ITenantService>();
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

        var controller = new HomeController(connectionStringManager, repo);
        ControllerTestBase.SetupHttpContext(controller, user);

        return controller;
    }

    private static HomeViewModel GetModel(IActionResult result)
    {
        var viewResult = (ViewResult)result;
        return (HomeViewModel)viewResult.Model!;
    }

    [Fact]
    public async Task Index_NotAuthenticated_RedirectsToLogin()
    {
        var anonymous = ClaimsPrincipalFactory.CreateAnonymous();
        var controller = CreateController(anonymous);

        var result = await controller.Index();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
    }

    [Fact]
    public async Task Index_Authenticated_ReturnsView()
    {
        var controller = CreateController();

        var result = await controller.Index();

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Index_SetsViewBagPermissions()
    {
        var controller = CreateController();

        var result = await controller.Index();

        var model = GetModel(result);
        Assert.True(model.DespatchWebPermission);
        Assert.True(model.BookJobPermission);
        Assert.True(model.BulkUploadPermission);
    }

    [Fact]
    public async Task Index_SetsViewBagContactId()
    {
        var controller = CreateController();

        var result = await controller.Index();

        var model = GetModel(result);
        Assert.Equal(1, model.ContactId);
    }

    [Fact]
    public async Task Index_CourierUser_ChecksAfterHours()
    {
        var courierUser = ClaimsPrincipalFactory.Create(
            email: "courier@test.com",
            isCourier: true,
            courierId: 1,
            contactId: "1",
            timeZone: "New Zealand Standard Time"
        );
        var controller = CreateController(courierUser);

        var result = await controller.Index();

        // Courier 1 has after-hours record in seed data
        var model = GetModel(result);
        Assert.True(model.ShowAfterHours);
    }

    [Fact]
    public async Task Index_NonCourierUser_ShowAfterHoursFalse()
    {
        var staffUser = ClaimsPrincipalFactory.Create(isCourier: false);
        var controller = CreateController(staffUser);

        var result = await controller.Index();

        var model = GetModel(result);
        Assert.False(model.ShowAfterHours);
    }

    [Fact]
    public async Task Index_SetsViewBagTenantCode()
    {
        var controller = CreateController();

        var result = await controller.Index();

        var model = GetModel(result);
        Assert.Equal("test", model.TenantCode);
    }
}
