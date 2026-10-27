using Hub.Controllers.Api;
using Hub.Interfaces;
using Hub.Tests.Helpers;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace Hub.Tests.Controllers.Api;

public class AuthApiControllerTests
{
    private static (AuthApiController Controller, IAccountService Service) Create()
    {
        var service = Substitute.For<IAccountService>();
        var controller = new AuthApiController(service);
        ControllerTestBase.SetupHttpContext(controller, ClaimsPrincipalFactory.CreateAnonymous());
        controller.Url = new UrlHelper(new ActionContext(
            controller.HttpContext, new Microsoft.AspNetCore.Routing.RouteData(), new ActionDescriptor()));
        return (controller, service);
    }

    private static LoginViewModel Model(string? returnUrl = null) =>
        new() { Email = "staff@test.com", Password = "TestPassword1!", ReturnUrl = returnUrl };

    private static T Body<T>(IActionResult result, int status)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, obj.StatusCode ?? 200);
        return Assert.IsAssignableFrom<T>(obj.Value);
    }

    // ---- login ----

    [Fact]
    public async Task Login_Success_Returns200WithRedirectUrl()
    {
        var (controller, service) = Create();
        service.LoginAsync(default!, default!, default).ReturnsForAnyArgs(AuthResult.Redirect("/"));

        var body = Body<RedirectResponse>(await controller.Login(Model()), 200);

        Assert.Equal("/", body.RedirectUrl);
    }

    [Fact]
    public async Task Login_Failure_Returns400WithError()
    {
        var (controller, service) = Create();
        service.LoginAsync(default!, default!, default).ReturnsForAnyArgs(AuthResult.Failure("Invalid login attempt."));

        var body = Body<ApiError>(await controller.Login(Model()), 400);

        Assert.Equal("Invalid login attempt.", body.Error);
    }

    [Fact]
    public async Task Login_LocalReturnUrl_IsPassedToTheService()
    {
        var (controller, service) = Create();
        service.LoginAsync(default!, default!, default).ReturnsForAnyArgs(AuthResult.Redirect("/x"));
        var model = Model("/connect/authorize?client_id=dfrnt-claude&state=abc");

        await controller.Login(model);

        await service.Received(1).LoginAsync(
            controller.HttpContext, model, "/connect/authorize?client_id=dfrnt-claude&state=abc");
    }

    [Theory]
    [InlineData("https://evil.example.test/")]
    [InlineData("//evil.example.test/path")]
    [InlineData("/\\evil.example.test")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Login_ReturnUrlsThatLeaveHub_AreDroppedBeforeTheService(string? returnUrl)
    {
        var (controller, service) = Create();
        service.LoginAsync(default!, default!, default).ReturnsForAnyArgs(AuthResult.Redirect("/"));
        var model = Model(returnUrl);

        await controller.Login(model);

        await service.Received(1).LoginAsync(controller.HttpContext, model, null);
    }

    // ---- logout ----

    [Fact]
    public async Task Logout_SignsOutAndReturnsLoginUrl()
    {
        var (controller, service) = Create();

        var body = Body<RedirectResponse>(await controller.Logout(), 200);

        Assert.Equal("/Account/Login", body.RedirectUrl);
        await service.Received(1).SignOutAsync(controller.HttpContext);
    }

    // ---- forgot password ----

    [Theory]
    [InlineData(ForgotPasswordOutcome.Sent)]
    [InlineData(ForgotPasswordOutcome.UserNotFound)]
    [InlineData(ForgotPasswordOutcome.ContactValidationFailed)]
    public async Task ForgotPassword_EveryOutcomeButCaptcha_Returns200Empty(ForgotPasswordOutcome outcome)
    {
        var (controller, service) = Create();
        service.ForgotPasswordAsync("staff@test.com", "tok").Returns(outcome);

        var result = await controller.ForgotPassword(
            new ForgotPasswordViewModel { Email = "staff@test.com", RecaptchaToken = "tok" });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("{}", System.Text.Json.JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public async Task ForgotPassword_CaptchaFailed_Returns400()
    {
        var (controller, service) = Create();
        service.ForgotPasswordAsync("staff@test.com", "tok").Returns(ForgotPasswordOutcome.CaptchaFailed);

        var body = Body<ApiError>(
            await controller.ForgotPassword(new ForgotPasswordViewModel { Email = "staff@test.com", RecaptchaToken = "tok" }),
            400);

        Assert.Equal("reCAPTCHA validation failed. Please try again.", body.Error);
    }

    // ---- reset password ----

    [Fact]
    public async Task ValidateResetCode_Valid_ReturnsEmail()
    {
        var (controller, service) = Create();
        service.GetResetEmailAsync("abc").Returns("reset@test.com");

        var body = Body<ResetCodeResponse>(await controller.ValidateResetCode("abc"), 200);

        Assert.Equal("reset@test.com", body.Email);
    }

    [Fact]
    public async Task ValidateResetCode_Invalid_Returns404()
    {
        var (controller, service) = Create();
        service.GetResetEmailAsync("bad").Returns((string?)null);

        Body<ApiError>(await controller.ValidateResetCode("bad"), 404);
    }

    [Fact]
    public async Task ResetPassword_Success_Returns200WithRedirectUrl()
    {
        var (controller, service) = Create();
        service.ResetPasswordAsync(default!, default!).ReturnsForAnyArgs(AuthResult.Redirect("/"));

        var body = Body<RedirectResponse>(
            await controller.ResetPassword(new ResetPasswordViewModel { Code = "c", Password = "NewStrong1!" }), 200);

        Assert.Equal("/", body.RedirectUrl);
    }

    [Fact]
    public async Task ResetPassword_Failure_Returns400WithError()
    {
        var (controller, service) = Create();
        service.ResetPasswordAsync(default!, default!)
            .ReturnsForAnyArgs(AuthResult.Failure("This reset link is invalid or has expired."));

        var body = Body<ApiError>(
            await controller.ResetPassword(new ResetPasswordViewModel { Code = "c", Password = "NewStrong1!" }), 400);

        Assert.Equal("This reset link is invalid or has expired.", body.Error);
    }
}
