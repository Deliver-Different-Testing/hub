using FluentAssertions;
using Hub.Controllers;
using Hub.Models;
using Hub.Repositories;
using Hub.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Moq;

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

    public void Dispose() => Environment.SetEnvironmentVariable("SQLCredentials", _originalCredentials);

    private static (HomeController controller, DynamicDespatchDbContext context) CreateController(
        System.Security.Claims.ClaimsPrincipal? user = null)
    {
        var context = TestDespatchContextFactory.CreateWithSeedData();
        var connectionStringManager = new ConnectionStringManager();
        var repo = new Repository(context);

        // Mock the stored procedures
        var mockProcs = new Mock<IDespatchContextProcedures>();
        mockProcs
            .Setup(p => p.RVW_stpValidateInternetPermissionsAsync(
                It.IsAny<int?>(), It.IsAny<OutputParameter<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RVW_stpValidateInternetPermissionsResult { InternetPermissionID = 2, ClientContactID = 1 },
                new RVW_stpValidateInternetPermissionsResult { InternetPermissionID = 12, ClientContactID = 1 },
                new RVW_stpValidateInternetPermissionsResult { InternetPermissionID = 11, ClientContactID = 1 }
            ]);
        context.Procedures = mockProcs.Object;

        var controller = new HomeController(connectionStringManager, repo);
        ControllerTestBase.SetupHttpContext(controller, user);

        return (controller, context);
    }

    [Fact]
    public async Task Index_NotAuthenticated_RedirectsToLogin()
    {
        var anonymous = ClaimsPrincipalFactory.CreateAnonymous();
        var (controller, _) = CreateController(anonymous);

        var result = await controller.Index();

        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be("Login");
        redirect.ControllerName.Should().Be("Account");
    }

    [Fact]
    public async Task Index_Authenticated_ReturnsView()
    {
        var (controller, _) = CreateController();

        var result = await controller.Index();

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public async Task Index_SetsViewBagPermissions()
    {
        var (controller, _) = CreateController();

        await controller.Index();

        ((bool)controller.ViewBag.DespatchWebPermission).Should().BeTrue();
        ((bool)controller.ViewBag.BookJobPermission).Should().BeTrue();
        ((bool)controller.ViewBag.BulkUploadPermission).Should().BeTrue();
    }

    [Fact]
    public async Task Index_SetsViewBagContactId()
    {
        var (controller, _) = CreateController();

        await controller.Index();

        ((int)controller.ViewBag.ContactID).Should().Be(1);
    }

    [Fact]
    public async Task Index_SetsViewBagGreetingString()
    {
        var (controller, _) = CreateController();

        await controller.Index();

        ((string)controller.ViewBag.GreetingString).Should().NotBeNullOrEmpty();
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
        var (controller, _) = CreateController(courierUser);

        await controller.Index();

        // Courier 1 has after-hours record in seed data
        ((bool)controller.ViewBag.ShowAfterHours).Should().BeTrue();
    }

    [Fact]
    public async Task Index_NonCourierUser_ShowAfterHoursFalse()
    {
        var staffUser = ClaimsPrincipalFactory.Create(isCourier: false);
        var (controller, _) = CreateController(staffUser);

        await controller.Index();

        ((bool)controller.ViewBag.ShowAfterHours).Should().BeFalse();
    }

    [Fact]
    public async Task Index_GreetingStringIsNotEmpty()
    {
        var (controller, _) = CreateController();

        await controller.Index();

        ((string)controller.ViewBag.GreetingString).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Index_SetsViewBagTenantCode()
    {
        var (controller, _) = CreateController();

        await controller.Index();

        ((string)controller.ViewBag.TenantCode).Should().Be("test");
    }
}
