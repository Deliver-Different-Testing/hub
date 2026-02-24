using FluentAssertions;
using Hub.Controllers;
using Hub.Models;
using Hub.Models.Master;
using Hub.Repositories;
using Hub.Shared;
using Hub.Tests.Helpers;
using Hub.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Moq;

namespace Hub.Tests.Controllers;

[Collection("EnvironmentVariables")]
public class AccountControllerTests : IDisposable
{
    private readonly string _originalCredentials;
    private readonly string _originalRecaptchaKey;

    public AccountControllerTests()
    {
        _originalCredentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? "";
        _originalRecaptchaKey = Environment.GetEnvironmentVariable("GoogleRecaptchaSecretKey") ?? "";
        Environment.SetEnvironmentVariable("SQLCredentials", ";User=test;Password=test;");
        Environment.SetEnvironmentVariable("GoogleRecaptchaSecretKey", "test-recaptcha-key");
        Environment.SetEnvironmentVariable("ReplyEmail", "noreply@test.com");
        Environment.SetEnvironmentVariable("ResetBaseLink", "https://test.com/reset");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SQLCredentials", _originalCredentials);
        Environment.SetEnvironmentVariable("GoogleRecaptchaSecretKey", _originalRecaptchaKey);
    }

    private static (AccountController controller, MasterContext masterCtx, DynamicDespatchDbContext despatchCtx) CreateController(
        System.Security.Claims.ClaimsPrincipal? user = null,
        HttpClient? httpClient = null)
    {
        var masterCtx = TestMasterContextFactory.CreateWithSeedData();
        var despatchCtx = TestDespatchContextFactory.CreateWithSeedData();

        // Set up passwords: hash "TestPassword1!" with salt "12345" for staff user
        var staffUser = masterCtx.Users.Find(1)!;
        var hash = PasswordHelper.HashPassword("TestPassword1!", "12345");
        staffUser.Password = hash;
        staffUser.Salt = "12345";

        var courierUser = masterCtx.Users.Find(2)!;
        courierUser.Password = PasswordHelper.HashPassword("CourierPass1!", "54321");
        courierUser.Salt = "54321";

        var legacyUser = masterCtx.Users.Find(3)!;
#pragma warning disable CS0618
        legacyUser.Password = PasswordHelper.HashPasswordLegacy("LegacyPass1!", "11111");
#pragma warning restore CS0618
        legacyUser.Salt = "11111";

        masterCtx.SaveChanges();

        // Mock stored procedures
        var mockProcs = new Mock<IDespatchContextProcedures>();
        mockProcs
            .Setup(p => p.RVW_stpValidateInternetPermissionsAsync(
                It.IsAny<int?>(), It.IsAny<OutputParameter<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        mockProcs
            .Setup(p => p.NET_stpContact_ResetPasswordAsync(
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<OutputParameter<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        despatchCtx.Procedures = mockProcs.Object;

        var connectionStringManager = new ConnectionStringManager();
        var authRepo = new AuthenticationRepository(masterCtx);
        var despatchRepo = new Repository(despatchCtx);

        httpClient ??= MockHttpMessageHandler.CreateReCaptchaClient();

        var controller = new AccountController(connectionStringManager, despatchRepo, authRepo, httpClient);
        ControllerTestBase.SetupHttpContext(controller, user ?? ClaimsPrincipalFactory.CreateAnonymous());

        return (controller, masterCtx, despatchCtx);
    }

    // Login GET tests
    [Fact]
    public void Login_Get_ReturnsViewWithReturnUrl()
    {
        var (controller, _, _) = CreateController();

        var result = controller.Login("/home") as ViewResult;

        result.Should().NotBeNull();
        Assert.Equal("/home", (string)controller.ViewBag.ReturnUrl);
    }

    // Login POST tests
    [Fact]
    public async Task Login_Post_InvalidModel_ReturnsView()
    {
        var (controller, _, _) = CreateController();
        controller.ModelState.AddModelError("Email", "Required");
        var model = new LoginViewModel { Email = "", Password = "" };

        var result = await controller.Login(model, null!);

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public async Task Login_Post_UserNotFound_ReturnsViewWithError()
    {
        var (controller, _, _) = CreateController();
        var model = new LoginViewModel { Email = "nobody@test.com", Password = "pass", IsCourierLogin = false };

        var result = await controller.Login(model, null!) as ViewResult;

        result.Should().NotBeNull();
        ((bool)controller.ViewBag.LoginFailed).Should().BeTrue();
    }

    [Fact]
    public async Task Login_Post_NullTenant_ReturnsViewWithError()
    {
        var (controller, masterCtx, _) = CreateController();
        // Create user with no tenant
        masterCtx.Users.Add(new User
        {
            UserId = 10, Email = "notenant@test.com", Password = "x", Salt = "x",
            CurrentTenantId = null, IsLegacyHash = false, IsCourier = false
        });
        await masterCtx.SaveChangesAsync();
        var model = new LoginViewModel { Email = "notenant@test.com", Password = "pass", IsCourierLogin = false };

        var result = await controller.Login(model, null!) as ViewResult;

        result.Should().NotBeNull();
        ((bool)controller.ViewBag.LoginFailed).Should().BeTrue();
    }

    [Fact]
    public async Task Login_Post_WrongPassword_ReturnsViewWithError()
    {
        var (controller, _, _) = CreateController();
        var model = new LoginViewModel { Email = "staff@test.com", Password = "WrongPassword!", IsCourierLogin = false };

        var result = await controller.Login(model, null!) as ViewResult;

        result.Should().NotBeNull();
        ((bool)controller.ViewBag.LoginFailed).Should().BeTrue();
    }

    [Fact]
    public async Task Login_Post_ValidStaffLogin_RedirectsToHome()
    {
        var (controller, _, _) = CreateController();
        var model = new LoginViewModel { Email = "staff@test.com", Password = "TestPassword1!", IsCourierLogin = false };

        var result = await controller.Login(model, null!);

        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Home");
    }

    [Fact]
    public async Task Login_Post_ValidCourierLogin_RedirectsToHome()
    {
        var (controller, _, _) = CreateController();
        var model = new LoginViewModel { Email = "courier@test.com", Password = "CourierPass1!", IsCourierLogin = true };

        var result = await controller.Login(model, null!);

        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be("Index");
    }

    [Fact]
    public async Task Login_Post_LegacyHashUpgrade_UpdatesPassword()
    {
        var (controller, masterCtx, _) = CreateController();
        var model = new LoginViewModel { Email = "legacy@test.com", Password = "LegacyPass1!", IsCourierLogin = false };

        await controller.Login(model, null!);

        var user = (await masterCtx.Users.FindAsync(3))!;
        user.IsLegacyHash.Should().BeFalse();
        // Password should now be the modern hash
        var expectedHash = PasswordHelper.HashPassword("LegacyPass1!", "11111");
        user.Password.Should().Be(expectedHash);
    }

    [Fact]
    public async Task Login_Post_CourierNotInDespatchDb_ReturnsError()
    {
        var (controller, masterCtx, _) = CreateController();
        // Create courier user whose email doesn't exist in despatch DB
        masterCtx.Users.Add(new User
        {
            UserId = 20, Email = "ghost-courier@test.com",
            Password = PasswordHelper.HashPassword("Pass1!", "33333"), Salt = "33333",
            CurrentTenantId = 1, IsLegacyHash = false, IsCourier = true
        });
        masterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 20, TenantId = 1, UserId = 20 });
        await masterCtx.SaveChangesAsync();
        var model = new LoginViewModel { Email = "ghost-courier@test.com", Password = "Pass1!", IsCourierLogin = true };

        var result = await controller.Login(model, null!) as ViewResult;

        result.Should().NotBeNull();
        ((bool)controller.ViewBag.LoginFailed).Should().BeTrue();
    }

    [Fact]
    public async Task Login_Post_StaffNotInDespatchDb_ReturnsError()
    {
        var (controller, masterCtx, _) = CreateController();
        masterCtx.Users.Add(new User
        {
            UserId = 21, Email = "ghost-staff@test.com",
            Password = PasswordHelper.HashPassword("Pass1!", "44444"), Salt = "44444",
            CurrentTenantId = 1, IsLegacyHash = false, IsCourier = false
        });
        masterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 21, TenantId = 1, UserId = 21 });
        await masterCtx.SaveChangesAsync();
        var model = new LoginViewModel { Email = "ghost-staff@test.com", Password = "Pass1!", IsCourierLogin = false };

        var result = await controller.Login(model, null!) as ViewResult;

        result.Should().NotBeNull();
        ((bool)controller.ViewBag.LoginFailed).Should().BeTrue();
    }

    [Fact]
    public async Task Login_Post_AlreadyAuthenticatedAsDifferentUser_ReturnsError()
    {
        var existingUser = ClaimsPrincipalFactory.Create(email: "other@test.com");
        var (controller, _, _) = CreateController(existingUser);
        var model = new LoginViewModel { Email = "staff@test.com", Password = "TestPassword1!", IsCourierLogin = false };

        var result = await controller.Login(model, null!) as ViewResult;

        result.Should().NotBeNull();
        ((bool)controller.ViewBag.LoginFailed).Should().BeTrue();
    }

    // ResetPassword GET tests
    [Fact]
    public async Task ResetPassword_Get_NullCode_Redirects()
    {
        var (controller, _, _) = CreateController();

        var result = await controller.ResetPassword((string)null!);

        result.Should().BeOfType<RedirectToActionResult>();
    }

    [Fact]
    public async Task ResetPassword_Get_InvalidCode_Redirects()
    {
        var (controller, _, _) = CreateController();

        var result = await controller.ResetPassword("invalid-code");

        result.Should().BeOfType<RedirectToActionResult>();
    }

    [Fact]
    public async Task ResetPassword_Get_ValidCode_ReturnsView()
    {
        var (controller, _, _) = CreateController();

        var result = await controller.ResetPassword("valid-reset-key") as ViewResult;

        result.Should().NotBeNull();
        var model = result.Model as ResetPasswordViewModel;
        model.Should().NotBeNull();
        model.Email.Should().Be("reset@test.com");
    }

    // ResetPassword POST tests
    [Fact]
    public async Task ResetPassword_Post_InvalidModel_ReturnsView()
    {
        var (controller, _, _) = CreateController();
        controller.ModelState.AddModelError("Password", "Required");
        var model = new ResetPasswordViewModel();

        var result = await controller.ResetPassword(model);

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public async Task ResetPassword_Post_InvalidCode_Redirects()
    {
        var (controller, _, _) = CreateController();
        var model = new ResetPasswordViewModel
        {
            Email = "reset@test.com", Password = "NewPass1!", ConfirmPassword = "NewPass1!", Code = "bad-code"
        };

        var result = await controller.ResetPassword(model);

        result.Should().BeOfType<RedirectToActionResult>();
    }

    [Fact]
    public async Task ResetPassword_Post_ValidReset_UpdatesPasswordAndClearsKey()
    {
        var (controller, masterCtx, despatchCtx) = CreateController();
        // Add the reset user's contact to despatch DB so full flow works
        despatchCtx.TucClientContacts.Add(new TucClientContact
        {
            UcctId = 10, UcctClientId = 1, UserName = "reset@test.com", UcctFirstname = "Reset",
            UcctSurname = "User", Active = true, HasEmail = true, ValidatedEmail = true,
            Created = DateTime.Now, CreatedBy = "test", LastModified = DateTime.Now, LastModifiedBy = "test"
        });
        await despatchCtx.SaveChangesAsync();

        var model = new ResetPasswordViewModel
        {
            Email = "reset@test.com", Password = "NewStrong1!", ConfirmPassword = "NewStrong1!", Code = "valid-reset-key"
        };

        await controller.ResetPassword(model);

        var user = (await masterCtx.Users.FindAsync(4))!;
        user.ResetKey.Should().BeNull();
        user.Password.Should().NotBe("RESETPASSWORD");
    }

    // ForgotPassword tests
    [Fact]
    public void ForgotPassword_Get_ReturnsView()
    {
        var (controller, _, _) = CreateController();

        var result = controller.ForgotPassword();

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public async Task ForgotPassword_Post_InvalidModel_ReturnsError()
    {
        var (controller, _, _) = CreateController();
        controller.ModelState.AddModelError("Email", "Required");
        var model = new ForgotPasswordViewModel { Email = "" };

        var result = await controller.ForgotPassword(model) as JsonResult;

        result.Should().NotBeNull();
        var value = result.Value;
        value.Should().BeEquivalentTo(new { success = false, message = "Please check your input and try again." });
    }

    [Fact]
    public async Task ForgotPassword_Post_RecaptchaFails_ReturnsError()
    {
        var httpClient = MockHttpMessageHandler.CreateReCaptchaClient(success: false, score: 0.1);
        var (controller, _, _) = CreateController(httpClient: httpClient);
        SetFormValues(controller, "fake-token");
        var model = new ForgotPasswordViewModel { Email = "staff@test.com" };

        var result = await controller.ForgotPassword(model) as JsonResult;

        result.Should().NotBeNull();
        var value = result.Value;
        value.Should().BeEquivalentTo(new { success = false, message = "reCAPTCHA validation failed. Please try again." });
    }

    [Fact]
    public async Task ForgotPassword_Post_UserNotFound_ReturnsError()
    {
        var (controller, _, _) = CreateController();
        SetFormValues(controller, "token");
        var model = new ForgotPasswordViewModel { Email = "nobody@test.com" };

        var result = await controller.ForgotPassword(model) as JsonResult;

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ForgotPassword_Post_ValidRequest_SetsResetKey()
    {
        var (controller, masterCtx, _) = CreateController();
        SetFormValues(controller, "token");
        var model = new ForgotPasswordViewModel { Email = "staff@test.com" };

        _ = await controller.ForgotPassword(model) as JsonResult;

        var user = (await masterCtx.Users.FindAsync(1))!;
        user.ResetKey.Should().NotBeNullOrEmpty();
    }

    // Logout tests
    [Fact]
    public async Task Logout_RedirectsToLogin()
    {
        var (controller, _, _) = CreateController(ClaimsPrincipalFactory.Create());

        var result = await controller.Logout();

        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be("login");
        redirect.ControllerName.Should().Be("Account");
    }

    // UpdateCurrentTenant tests
    [Fact]
    public async Task UpdateCurrentTenant_NoClaim_ReturnsFailure()
    {
        var anonymous = ClaimsPrincipalFactory.CreateAnonymous();
        var (controller, _, _) = CreateController(anonymous);

        var result = await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 1 }) as JsonResult;

        result!.Value.Should().BeEquivalentTo(new { success = false, message = "User not found" });
    }

    [Fact]
    public async Task UpdateCurrentTenant_NullModel_ReturnsFailure()
    {
        var (controller, _, _) = CreateController(ClaimsPrincipalFactory.Create());

        var result = await controller.UpdateCurrentTenant(null!) as JsonResult;

        result!.Value.Should().BeEquivalentTo(new { success = false, message = "Invalid tenant ID" });
    }

    [Fact]
    public async Task UpdateCurrentTenant_ZeroTenantId_ReturnsFailure()
    {
        var (controller, _, _) = CreateController(ClaimsPrincipalFactory.Create());

        var result = await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 0 }) as JsonResult;

        result!.Value.Should().BeEquivalentTo(new { success = false, message = "Invalid tenant ID" });
    }

    [Fact]
    public async Task UpdateCurrentTenant_UpdateFails_ReturnsFailure()
    {
        var user = ClaimsPrincipalFactory.Create(userId: 2);
        var (controller, _, _) = CreateController(user);

        // User 2 not associated with tenant 2
        var result = await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 2 }) as JsonResult;

        result!.Value.Should().BeEquivalentTo(new { success = false, message = "Update database failed" });
    }

    [Fact]
    public async Task UpdateCurrentTenant_ValidUpdate_ReturnsSuccess()
    {
        var user = ClaimsPrincipalFactory.Create(userId: 1, email: "staff@test.com");
        var (controller, _, _) = CreateController(user);

        var result = await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 2 }) as JsonResult;

        // Staff user (userId=1) is associated with both tenants
        result!.Value.Should().BeEquivalentTo(new { success = true });
    }

    // Settings tests
    [Fact]
    public async Task Settings_NoClaim_ReturnsView()
    {
        var anonymous = ClaimsPrincipalFactory.CreateAnonymous();
        var (controller, _, _) = CreateController(anonymous);

        var result = await controller.Settings() as ViewResult;

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Settings_UserNotFound_ReturnsEmptyView()
    {
        var user = ClaimsPrincipalFactory.Create(email: "nobody@test.com");
        var (controller, _, _) = CreateController(user);

        var result = await controller.Settings() as ViewResult;

        result.Should().NotBeNull();
        var model = result.Model as List<TenantUserSettingViewModel>;
        // Should have at least the hardcoded "Test Setting" added in Settings action
        model.Should().NotBeNull();
    }

    [Fact]
    public async Task Settings_ValidUser_ReturnsSettings()
    {
        var user = ClaimsPrincipalFactory.Create(email: "staff@test.com");
        var (controller, _, _) = CreateController(user);

        var result = await controller.Settings() as ViewResult;

        result.Should().NotBeNull();
        var model = result.Model as List<TenantUserSettingViewModel>;
        model.Should().NotBeNull();
        // Should include "Theme" from seed data + "Test Setting" hardcoded
        model.Should().Contain(s => s.Name == "Theme");
        model.Should().Contain(s => s.Name == "Test Setting");
    }

    private static void SetFormValues(AccountController controller, string reCaptchaToken)
    {
        var formCollection = new FormCollection(
            new Dictionary<string, StringValues>
            {
                { "g-recaptcha-response", new StringValues(reCaptchaToken) }
            });
        controller.HttpContext.Request.ContentType = "application/x-www-form-urlencoded";
        controller.HttpContext.Request.Form = formCollection;
    }
}
