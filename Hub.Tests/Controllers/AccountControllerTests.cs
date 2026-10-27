using Hub.Controllers;
using Hub.Interfaces;
using Hub.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Controllers;

public class AccountControllerTests
{
    private static (AccountController Controller, IAccountService Service) Create()
    {
        var service = Substitute.For<IAccountService>();
        var controller = new AccountController(service);
        ControllerTestBase.SetupHttpContext(controller, ClaimsPrincipalFactory.CreateAnonymous());
        return (controller, service);
    }

    [Fact]
    public async Task CreditCard_Success_RedirectsToTheServiceUrl()
    {
        var (controller, service) = Create();
        service.CreditCardLoginAsync(controller.HttpContext).Returns(AuthResult.Redirect("https://booking.example.com"));

        var redirect = Assert.IsType<RedirectResult>(await controller.CreditCard());

        Assert.Equal("https://booking.example.com", redirect.Url);
    }

    [Fact]
    public async Task CreditCard_Failure_RedirectsToTheSpaLoginWithTheError()
    {
        var (controller, service) = Create();
        service.CreditCardLoginAsync(controller.HttpContext)
            .Returns(AuthResult.Failure("Auto-login failed. Please login manually."));

        var redirect = Assert.IsType<RedirectResult>(await controller.CreditCard());

        Assert.Equal("/Account/Login?error=Auto-login%20failed.%20Please%20login%20manually.", redirect.Url);
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_Accepted_RedirectsToHome()
    {
        var (controller, service) = Create();
        service.AcceptTenantSwitchTokenAsync(controller.HttpContext, "tok").Returns(true);

        var redirect = Assert.IsType<RedirectResult>(await controller.AcceptTenantSwitchToken("tok"));

        Assert.Equal("/", redirect.Url);
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_Rejected_RedirectsToLogin()
    {
        var (controller, service) = Create();
        service.AcceptTenantSwitchTokenAsync(controller.HttpContext, "tok").Returns(false);

        var redirect = Assert.IsType<RedirectResult>(await controller.AcceptTenantSwitchToken("tok"));

        Assert.Equal("/Account/Login", redirect.Url);
    }
}
