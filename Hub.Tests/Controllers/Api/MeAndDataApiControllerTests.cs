using Hub.Controllers.Api;
using Hub.Interfaces;
using Hub.Tests.Helpers;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Controllers.Api;

public class MeAndDataApiControllerTests
{
    private static T Body<T>(IActionResult result, int status)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, obj.StatusCode ?? 200);
        return Assert.IsAssignableFrom<T>(obj.Value);
    }

    // ---- me ----

    private static (MeApiController Controller, ICurrentUserService Users, IAccountService Accounts) CreateMe()
    {
        var users = Substitute.For<ICurrentUserService>();
        var accounts = Substitute.For<IAccountService>();
        var controller = new MeApiController(users, accounts);
        ControllerTestBase.SetupHttpContext(controller, ClaimsPrincipalFactory.Create());
        return (controller, users, accounts);
    }

    [Fact]
    public async Task Me_Get_ReturnsTheCurrentUser()
    {
        var (controller, users, _) = CreateMe();
        var me = new MeResponse("a@b.c", "A B", false, new MeTenant(1, "Urgent", "urgent", "NZ"), []);
        users.GetMeAsync(controller.User).Returns(me);

        Assert.Same(me, Body<MeResponse>(await controller.Get(), 200));
    }

    [Fact]
    public async Task Me_UpdateCurrentTenant_Success_ReturnsRedirectUrl()
    {
        var (controller, _, accounts) = CreateMe();
        accounts.UpdateCurrentTenantAsync(controller.HttpContext, 2)
            .Returns(new UpdateTenantResult(true, null, "https://hub.second.deliverdifferent.com/x"));

        var body = Body<RedirectResponse>(
            await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 2 }), 200);

        Assert.Equal("https://hub.second.deliverdifferent.com/x", body.RedirectUrl);
    }

    [Fact]
    public async Task Me_UpdateCurrentTenant_NoRedirect_ReturnsNullRedirectUrl()
    {
        var (controller, _, accounts) = CreateMe();
        accounts.UpdateCurrentTenantAsync(controller.HttpContext, 2).Returns(new UpdateTenantResult(true, null, null));

        var body = Body<RedirectResponse>(
            await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 2 }), 200);

        Assert.Null(body.RedirectUrl);
    }

    [Fact]
    public async Task Me_UpdateCurrentTenant_Failure_Returns400WithMessage()
    {
        var (controller, _, accounts) = CreateMe();
        accounts.UpdateCurrentTenantAsync(controller.HttpContext, 2)
            .Returns(UpdateTenantResult.Fail("You don't have access to that tenant."));

        var body = Body<ApiError>(await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 2 }), 400);

        Assert.Equal("You don't have access to that tenant.", body.Error);
    }

    [Fact]
    public async Task Me_Settings_ReturnsTheUsersSettings()
    {
        var (controller, _, accounts) = CreateMe();
        IReadOnlyList<TenantUserSettingViewModel> settings = [new() { Id = 1, Name = "Theme", Value = "dark" }];
        accounts.GetSettingsAsync(controller.User).Returns(settings);

        Assert.Same(settings, Body<IReadOnlyList<TenantUserSettingViewModel>>(await controller.GetSettings(), 200));
    }

    [Fact]
    public async Task Me_ApiKey_Success_ReturnsKey()
    {
        var (controller, _, accounts) = CreateMe();
        accounts.GenerateApiKeyAsync(controller.User).Returns(new GenerateApiKeyResult("jwt"));

        Assert.Equal("jwt", Body<ApiKeyResponse>(await controller.GenerateApiKey(), 200).ApiKey);
    }

    [Fact]
    public async Task Me_ApiKey_Failure_Returns400()
    {
        var (controller, _, accounts) = CreateMe();
        accounts.GenerateApiKeyAsync(controller.User).Returns(new GenerateApiKeyResult(null));

        Assert.Equal("Failed to generate API key", Body<ApiError>(await controller.GenerateApiKey(), 400).Error);
    }

    // ---- home ----

    [Fact]
    public async Task Home_Get_ReturnsTiles()
    {
        var service = Substitute.For<IHomeService>();
        var controller = new HomeApiController(service);
        ControllerTestBase.SetupHttpContext(controller, ClaimsPrincipalFactory.Create());
        var home = new HomeResponse("a@b.c", "urgent", DateTime.UnixEpoch, false, false, []);
        service.GetHomeAsync(controller.User).Returns(home);

        Assert.Same(home, Body<HomeResponse>(await controller.Get(), 200));
    }

    [Fact]
    public async Task Home_Get_MissingClaims_Returns401()
    {
        var service = Substitute.For<IHomeService>();
        var controller = new HomeApiController(service);
        ControllerTestBase.SetupHttpContext(controller, ClaimsPrincipalFactory.Create());
        service.GetHomeAsync(controller.User).Returns((HomeResponse?)null);

        Body<ApiError>(await controller.Get(), 401);
    }

    // ---- fuel surcharge ----

    private static (FuelSurchargeApiController Controller, IFuelSurchargeService Service) CreateFuel()
    {
        var service = Substitute.For<IFuelSurchargeService>();
        var controller = new FuelSurchargeApiController(service);
        ControllerTestBase.SetupHttpContext(controller, ClaimsPrincipalFactory.Create());
        return (controller, service);
    }

    [Fact]
    public async Task FuelSurcharge_Ok_ReturnsTheCard()
    {
        var (controller, service) = CreateFuel();
        var card = new FuelSurchargeCardViewModel { HasData = true };
        service.GetCardAsync(controller.User, CancellationToken.None)
            .Returns(new FuelSurchargeResult(FuelSurchargeStatus.Ok, card));

        Assert.Same(card, Body<FuelSurchargeCardViewModel>(await controller.Get(CancellationToken.None), 200));
    }

    [Fact]
    public async Task FuelSurcharge_NotAvailable_Returns403()
    {
        var (controller, service) = CreateFuel();
        service.GetCardAsync(controller.User, CancellationToken.None)
            .Returns(new FuelSurchargeResult(FuelSurchargeStatus.NotAvailable));

        Body<ApiError>(await controller.Get(CancellationToken.None), 403);
    }

    [Fact]
    public async Task FuelSurcharge_NotAuthenticated_Returns401()
    {
        var (controller, service) = CreateFuel();
        service.GetCardAsync(controller.User, CancellationToken.None)
            .Returns(new FuelSurchargeResult(FuelSurchargeStatus.NotAuthenticated));

        Body<ApiError>(await controller.Get(CancellationToken.None), 401);
    }

    // ---- config ----

    [Fact]
    public void Config_ReturnsTheRecaptchaSiteKey()
    {
        var original = Environment.GetEnvironmentVariable("GoogleRecaptchaSiteKey");
        Environment.SetEnvironmentVariable("GoogleRecaptchaSiteKey", "site-key");
        try
        {
            Assert.Equal("site-key", Body<ConfigResponse>(new ConfigApiController().Get(), 200).RecaptchaSiteKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GoogleRecaptchaSiteKey", original);
        }
    }

    [Fact]
    public void Config_NoSiteKey_ReturnsNull()
    {
        var original = Environment.GetEnvironmentVariable("GoogleRecaptchaSiteKey");
        Environment.SetEnvironmentVariable("GoogleRecaptchaSiteKey", null);
        try
        {
            Assert.Null(Body<ConfigResponse>(new ConfigApiController().Get(), 200).RecaptchaSiteKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GoogleRecaptchaSiteKey", original);
        }
    }
}
