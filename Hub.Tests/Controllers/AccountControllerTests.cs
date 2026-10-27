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

    [Theory]
    [InlineData("Auto-login failed. Please login manually.")]
    [InlineData("Auto-login not configured.")]
    public async Task CreditCard_Failure_RedirectsToTheSpaLoginWithAFixedErrorCode(string error)
    {
        var (controller, service) = Create();
        service.CreditCardLoginAsync(controller.HttpContext).Returns(AuthResult.Failure(error));

        var redirect = Assert.IsType<RedirectResult>(await controller.CreditCard());

        // A code, never message text: the login page only shows messages it owns.
        Assert.Equal("/Account/Login?error=autologin-failed", redirect.Url);
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
