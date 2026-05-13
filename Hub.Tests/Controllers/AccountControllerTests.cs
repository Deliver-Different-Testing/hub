using System.Security.Cryptography;
using System.Text.Json;
using Hub.Controllers;
using Hub.Interfaces;
using Hub.Models;
using Hub.Models.Master;
using Hub.Repositories;
using Hub.Shared;
using Hub.Tests.Helpers;
using Hub.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using NSubstitute;

namespace Hub.Tests.Controllers;

[Collection("EnvironmentVariables")]
public class AccountControllerTests : IDisposable
{
    private readonly string _originalCredentials;
    private readonly string _originalRecaptchaKey;

    public AccountControllerTests()
    {
        _originalCredentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        _originalRecaptchaKey = Environment.GetEnvironmentVariable("GoogleRecaptchaSecretKey") ?? string.Empty;
        Environment.SetEnvironmentVariable("SQLCredentials", ";User=test;Password=test;");
        Environment.SetEnvironmentVariable("GoogleRecaptchaSecretKey", "test-recaptcha-key");
        Environment.SetEnvironmentVariable("ReplyEmail", "noreply@test.com");
        Environment.SetEnvironmentVariable("ResetBaseLink", "https://test.com/reset");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SQLCredentials", _originalCredentials);
        Environment.SetEnvironmentVariable("GoogleRecaptchaSecretKey", _originalRecaptchaKey);
        GC.SuppressFinalize(this);
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

        var npUser = masterCtx.Users.Find(5)!;
        npUser.Password = PasswordHelper.HashPassword("NpPass1!", "55555");
        npUser.Salt = "55555";

        masterCtx.SaveChanges();

        // Mock stored procedures
        var mockProcs = Substitute.For<IDespatchContextProcedures>();
        mockProcs.RVW_stpValidateInternetPermissionsAsync(
                Arg.Any<int?>(), Arg.Any<OutputParameter<int>>(), Arg.Any<CancellationToken>())
            .Returns([]);
        mockProcs.NET_stpContact_ResetPasswordAsync(
                Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<OutputParameter<int>>(), Arg.Any<CancellationToken>())
            .Returns(1);
        despatchCtx.Procedures = mockProcs;

        var connectionStringManager = new ConnectionStringManager();
        var authRepo = new AuthenticationRepository(masterCtx);
        var tenantService = Substitute.For<ITenantService>();
        var despatchRepo = new Repository(despatchCtx, tenantService);

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

        Assert.NotNull(result);
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

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Login_Post_UserNotFound_ReturnsViewWithError()
    {
        var (controller, _, _) = CreateController();
        var model = new LoginViewModel { Email = "nobody@test.com", Password = "pass", IsCourierLogin = false };

        var result = await controller.Login(model, null!) as ViewResult;

        Assert.NotNull(result);
        Assert.True((bool)controller.ViewBag.LoginFailed);
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
        await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
        var model = new LoginViewModel { Email = "notenant@test.com", Password = "pass", IsCourierLogin = false };

        var result = await controller.Login(model, null!) as ViewResult;

        Assert.NotNull(result);
        Assert.True((bool)controller.ViewBag.LoginFailed);
    }

    [Fact]
    public async Task Login_Post_WrongPassword_ReturnsViewWithError()
    {
        var (controller, _, _) = CreateController();
        var model = new LoginViewModel { Email = "staff@test.com", Password = "WrongPassword!", IsCourierLogin = false };

        var result = await controller.Login(model, null!) as ViewResult;

        Assert.NotNull(result);
        Assert.True((bool)controller.ViewBag.LoginFailed);
    }

    [Fact]
    public async Task Login_Post_ValidStaffLogin_RedirectsToHome()
    {
        var (controller, _, _) = CreateController();
        var model = new LoginViewModel { Email = "staff@test.com", Password = "TestPassword1!", IsCourierLogin = false };

        var result = await controller.Login(model, null!);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }

    [Fact]
    public async Task Login_Post_ValidCourierLogin_RedirectsToHome()
    {
        var (controller, _, _) = CreateController();
        var model = new LoginViewModel { Email = "courier@test.com", Password = "CourierPass1!", IsCourierLogin = true };

        var result = await controller.Login(model, null!);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task Login_Post_NetworkPartner_IssuesIsNetworkPartnerClaim()
    {
        var (controller, _, _) = CreateController();
        var model = new LoginViewModel { Email = "np@test.com", Password = "NpPass1!", IsCourierLogin = false };

        var result = await controller.Login(model, null!);

        Assert.IsType<RedirectToActionResult>(result);

        var authService = (Microsoft.AspNetCore.Authentication.IAuthenticationService)
            controller.HttpContext.RequestServices.GetService(
                typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService))!;

        await authService.Received(1).SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Is<System.Security.Claims.ClaimsPrincipal>(p =>
                p.HasClaim("IsNetworkPartner", "True") &&
                p.HasClaim("IsCourier", "False")),
            Arg.Any<Microsoft.AspNetCore.Authentication.AuthenticationProperties>());
    }

    [Fact]
    public async Task Login_Post_StaffLogin_IsNetworkPartnerClaimIsFalse()
    {
        var (controller, _, _) = CreateController();
        var model = new LoginViewModel { Email = "staff@test.com", Password = "TestPassword1!", IsCourierLogin = false };

        var result = await controller.Login(model, null!);

        Assert.IsType<RedirectToActionResult>(result);

        var authService = (Microsoft.AspNetCore.Authentication.IAuthenticationService)
            controller.HttpContext.RequestServices.GetService(
                typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService))!;

        await authService.Received(1).SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Is<System.Security.Claims.ClaimsPrincipal>(p =>
                p.HasClaim("IsNetworkPartner", "False")),
            Arg.Any<Microsoft.AspNetCore.Authentication.AuthenticationProperties>());
    }

    [Fact]
    public async Task Login_Post_LegacyHashUpgrade_UpdatesPassword()
    {
        var (controller, masterCtx, _) = CreateController();
        var model = new LoginViewModel { Email = "legacy@test.com", Password = "LegacyPass1!", IsCourierLogin = false };

        await controller.Login(model, null!);

        var user = (await masterCtx.Users.FindAsync([3], TestContext.Current.CancellationToken))!;
        Assert.False(user.IsLegacyHash);
        // Password should now be the modern hash
        var expectedHash = PasswordHelper.HashPassword("LegacyPass1!", "11111");
        Assert.Equal(expectedHash, user.Password);
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
        await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
        var model = new LoginViewModel { Email = "ghost-courier@test.com", Password = "Pass1!", IsCourierLogin = true };

        var result = await controller.Login(model, null!) as ViewResult;

        Assert.NotNull(result);
        Assert.True((bool)controller.ViewBag.LoginFailed);
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
        await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
        var model = new LoginViewModel { Email = "ghost-staff@test.com", Password = "Pass1!", IsCourierLogin = false };

        var result = await controller.Login(model, null!) as ViewResult;

        Assert.NotNull(result);
        Assert.True((bool)controller.ViewBag.LoginFailed);
    }

    [Fact]
    public async Task Login_Post_AlreadyAuthenticatedAsDifferentUser_ReturnsError()
    {
        var existingUser = ClaimsPrincipalFactory.Create(email: "other@test.com");
        var (controller, _, _) = CreateController(existingUser);
        var model = new LoginViewModel { Email = "staff@test.com", Password = "TestPassword1!", IsCourierLogin = false };

        var result = await controller.Login(model, null!) as ViewResult;

        Assert.NotNull(result);
        Assert.True((bool)controller.ViewBag.LoginFailed);
    }

    [Fact]
    public async Task Login_Post_AlreadyAuthenticatedAsSameUser_ProceedsWithLogin()
    {
        var existingUser = ClaimsPrincipalFactory.Create(email: "staff@test.com");
        var (controller, _, _) = CreateController(existingUser);
        var model = new LoginViewModel { Email = "staff@test.com", Password = "TestPassword1!", IsCourierLogin = false };

        var result = await controller.Login(model, null!);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
    }

    // ResetPassword GET tests
    [Fact]
    public async Task ResetPassword_Get_NullCode_Redirects()
    {
        var (controller, _, _) = CreateController();

        var result = await controller.ResetPassword((string)null!);

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task ResetPassword_Get_InvalidCode_Redirects()
    {
        var (controller, _, _) = CreateController();

        var result = await controller.ResetPassword("invalid-code");

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task ResetPassword_Get_ValidCode_ReturnsView()
    {
        var (controller, _, _) = CreateController();

        var result = await controller.ResetPassword("valid-reset-key") as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsType<ResetPasswordViewModel>(result.Model);
        Assert.Equal("reset@test.com", model.Email);
    }

    // ResetPassword POST tests
    [Fact]
    public async Task ResetPassword_Post_InvalidModel_ReturnsView()
    {
        var (controller, _, _) = CreateController();
        controller.ModelState.AddModelError("Password", "Required");
        var model = new ResetPasswordViewModel { Email = "", Code = "" };

        var result = await controller.ResetPassword(model);

        Assert.IsType<ViewResult>(result);
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

        Assert.IsType<RedirectToActionResult>(result);
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
        await despatchCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var model = new ResetPasswordViewModel
        {
            Email = "reset@test.com", Password = "NewStrong1!", ConfirmPassword = "NewStrong1!", Code = "valid-reset-key"
        };

        await controller.ResetPassword(model);

        var user = (await masterCtx.Users.FindAsync([4], TestContext.Current.CancellationToken))!;
        Assert.Null(user.ResetKey);
        Assert.NotEqual("RESETPASSWORD", user.Password);
    }

    // ForgotPassword tests
    [Fact]
    public void ForgotPassword_Get_ReturnsView()
    {
        var (controller, _, _) = CreateController();

        var result = controller.ForgotPassword();

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task ForgotPassword_Post_InvalidModel_ReturnsError()
    {
        var (controller, _, _) = CreateController();
        controller.ModelState.AddModelError("Email", "Required");
        var model = new ForgotPasswordViewModel { Email = "" };

        var result = await controller.ForgotPassword(model) as JsonResult;

        Assert.NotNull(result);
        var value = result.Value;
        AssertHelper.JsonEquivalent(new { success = false, message = "Please check your input and try again." }, value);
    }

    [Fact]
    public async Task ForgotPassword_Post_RecaptchaFails_ReturnsError()
    {
        var httpClient = MockHttpMessageHandler.CreateReCaptchaClient(success: false, score: 0.1);
        var (controller, _, _) = CreateController(httpClient: httpClient);
        SetFormValues(controller, "fake-token");
        var model = new ForgotPasswordViewModel { Email = "staff@test.com" };

        var result = await controller.ForgotPassword(model) as JsonResult;

        Assert.NotNull(result);
        var value = result.Value;
        AssertHelper.JsonEquivalent(new { success = false, message = "reCAPTCHA validation failed. Please try again." }, value);
    }

    [Fact]
    public async Task ForgotPassword_Post_UserNotFound_ReturnsError()
    {
        var (controller, _, _) = CreateController();
        SetFormValues(controller, "token");
        var model = new ForgotPasswordViewModel { Email = "nobody@test.com" };

        var result = await controller.ForgotPassword(model) as JsonResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ForgotPassword_Post_ValidRequest_SetsResetKey()
    {
        var (controller, masterCtx, _) = CreateController();
        SetFormValues(controller, "token");
        var model = new ForgotPasswordViewModel { Email = "staff@test.com" };

        _ = await controller.ForgotPassword(model) as JsonResult;

        var user = (await masterCtx.Users.FindAsync([1], TestContext.Current.CancellationToken))!;
        Assert.False(string.IsNullOrEmpty(user.ResetKey));
    }

    // Logout tests
    [Fact]
    public async Task Logout_RedirectsToLogin()
    {
        var (controller, _, _) = CreateController(ClaimsPrincipalFactory.Create());

        var result = await controller.Logout();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("login", redirect.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
    }

    // UpdateCurrentTenant tests
    [Fact]
    public async Task UpdateCurrentTenant_NoClaim_ReturnsFailure()
    {
        var anonymous = ClaimsPrincipalFactory.CreateAnonymous();
        var (controller, _, _) = CreateController(anonymous);

        var result = await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 1 }) as JsonResult;

        AssertHelper.JsonEquivalent(new { success = false, message = "User not found" }, result!.Value);
    }

    [Fact]
    public async Task UpdateCurrentTenant_NullModel_ReturnsFailure()
    {
        var (controller, _, _) = CreateController(ClaimsPrincipalFactory.Create());

        var result = await controller.UpdateCurrentTenant(null!) as JsonResult;

        AssertHelper.JsonEquivalent(new { success = false, message = "Invalid tenant ID" }, result!.Value);
    }

    [Fact]
    public async Task UpdateCurrentTenant_ZeroTenantId_ReturnsFailure()
    {
        var (controller, _, _) = CreateController(ClaimsPrincipalFactory.Create());

        var result = await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 0 }) as JsonResult;

        AssertHelper.JsonEquivalent(new { success = false, message = "Invalid tenant ID" }, result!.Value);
    }

    [Fact]
    public async Task UpdateCurrentTenant_UpdateFails_ReturnsFailure()
    {
        var user = ClaimsPrincipalFactory.Create(userId: 2);
        var (controller, _, _) = CreateController(user);

        // User 2 not associated with tenant 2
        var result = await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 2 }) as JsonResult;

        AssertHelper.JsonEquivalent(new { success = false, message = "Update database failed" }, result!.Value);
    }

    [Fact]
    public async Task UpdateCurrentTenant_ValidUpdate_ReturnsSuccess()
    {
        var user = ClaimsPrincipalFactory.Create(userId: 1, email: "staff@test.com");
        var (controller, _, _) = CreateController(user);

        var result = await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 2 }) as JsonResult;

        // Staff user (userId=1) is associated with both tenants
        AssertHelper.JsonEquivalent(new { success = true }, result!.Value);
    }

    // Settings tests
    [Fact]
    public async Task Settings_NoClaim_ReturnsView()
    {
        var anonymous = ClaimsPrincipalFactory.CreateAnonymous();
        var (controller, _, _) = CreateController(anonymous);

        var result = await controller.Settings() as ViewResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Settings_UserNotFound_ReturnsEmptyView()
    {
        var user = ClaimsPrincipalFactory.Create(email: "nobody@test.com");
        var (controller, _, _) = CreateController(user);

        var result = await controller.Settings() as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsType<IReadOnlyList<TenantUserSettingViewModel>>(result.Model, exactMatch: false);
        Assert.Empty(model);
    }

    [Fact]
    public async Task Settings_ValidUser_ReturnsSettings()
    {
        var user = ClaimsPrincipalFactory.Create(email: "staff@test.com");
        var (controller, _, _) = CreateController(user);

        var result = await controller.Settings() as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsAssignableFrom<IReadOnlyList<TenantUserSettingViewModel>>(result.Model);
        Assert.Contains(model, s => s.Name == "Theme");
    }

    // GenerateApiKey tests
    [Fact]
    public async Task GenerateApiKey_MissingClaims_ReturnsFailure()
    {
        var anonymous = ClaimsPrincipalFactory.CreateAnonymous();
        var (controller, _, _) = CreateController(anonymous);

        var result = await controller.GenerateApiKey() as JsonResult;

        Assert.NotNull(result);
        AssertHelper.JsonEquivalent(new { success = false, message = "Failed to generate API key" }, result.Value);
    }

    [Fact]
    public async Task GenerateApiKey_ValidUser_ReturnsApiKey()
    {
        Environment.SetEnvironmentVariable("JWTSecretKey", "ThisIsASecretKeyForTestingThatMustBeLongEnough123!");
        Environment.SetEnvironmentVariable("ClaimsKey", Convert.ToBase64String(new byte[32]));
        Environment.SetEnvironmentVariable("Issuer", "test-issuer");
        Environment.SetEnvironmentVariable("Audience", "test-audience");
        try
        {
            var user = ClaimsPrincipalFactory.Create(email: "staff@test.com");
            var (controller, _, _) = CreateController(user);

            var result = await controller.GenerateApiKey() as JsonResult;

            Assert.NotNull(result);
            var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
            Assert.Contains("\"success\":true", json);
            Assert.Contains("apiKey", json);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JWTSecretKey", null);
            Environment.SetEnvironmentVariable("ClaimsKey", null);
            Environment.SetEnvironmentVariable("Issuer", null);
            Environment.SetEnvironmentVariable("Audience", null);
        }
    }

    // SetTenantConnectionString branch: missing SQLCredentials
    [Fact]
    public async Task Login_Post_MissingSQLCredentials_Throws()
    {
        var original = Environment.GetEnvironmentVariable("SQLCredentials");
        Environment.SetEnvironmentVariable("SQLCredentials", "");
        try
        {
            var (controller, _, _) = CreateController();
            var model = new LoginViewModel
                { Email = "staff@test.com", Password = "TestPassword1!", IsCourierLogin = false };

            await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Login(model, null!));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SQLCredentials", original);
        }
    }

    // CreditCard tests
    [Fact]
    public async Task CreditCard_MissingCredentials_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("CreditCardEmail", string.Empty);
        Environment.SetEnvironmentVariable("CreditCardPassword", string.Empty);
        try
        {
            var (controller, _, _) = CreateController();

            var result = await controller.CreditCard();

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CreditCardEmail", null);
            Environment.SetEnvironmentVariable("CreditCardPassword", null);
        }
    }

    [Fact]
    public async Task CreditCard_UserNotFound_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("CreditCardEmail", "nobody@test.com");
        Environment.SetEnvironmentVariable("CreditCardPassword", "pass");
        try
        {
            var (controller, _, _) = CreateController();

            var result = await controller.CreditCard();

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CreditCardEmail", null);
            Environment.SetEnvironmentVariable("CreditCardPassword", null);
        }
    }

    [Fact]
    public async Task CreditCard_NullTenant_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("CreditCardEmail", "notenant@test.com");
        Environment.SetEnvironmentVariable("CreditCardPassword", "Pass1!");
        try
        {
            var (controller, masterCtx, _) = CreateController();
            masterCtx.Users.Add(new User
            {
                UserId = 30, Email = "notenant@test.com",
                Password = PasswordHelper.HashPassword("Pass1!", "99999"), Salt = "99999",
                CurrentTenantId = null, IsLegacyHash = false, IsCourier = false
            });
            await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

            var result = await controller.CreditCard();

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CreditCardEmail", null);
            Environment.SetEnvironmentVariable("CreditCardPassword", null);
        }
    }

    [Fact]
    public async Task CreditCard_WrongPassword_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("CreditCardEmail", "staff@test.com");
        Environment.SetEnvironmentVariable("CreditCardPassword", "WrongPassword!");
        try
        {
            var (controller, _, _) = CreateController();

            var result = await controller.CreditCard();

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CreditCardEmail", null);
            Environment.SetEnvironmentVariable("CreditCardPassword", null);
        }
    }

    [Fact]
    public async Task CreditCard_DespatchUserNotFound_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("CreditCardEmail", "nodespatch@test.com");
        Environment.SetEnvironmentVariable("CreditCardPassword", "Pass1!");
        try
        {
            var (controller, masterCtx, _) = CreateController();
            masterCtx.Users.Add(new User
            {
                UserId = 31, Email = "nodespatch@test.com",
                Password = PasswordHelper.HashPassword("Pass1!", "88888"), Salt = "88888",
                CurrentTenantId = 1, IsLegacyHash = false, IsCourier = false
            });
            masterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 31, TenantId = 1, UserId = 31 });
            await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

            var result = await controller.CreditCard();

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CreditCardEmail", null);
            Environment.SetEnvironmentVariable("CreditCardPassword", null);
        }
    }

    [Fact]
    public async Task CreditCard_ValidLogin_NoTenantUrl_RedirectsToHome()
    {
        Environment.SetEnvironmentVariable("CreditCardEmail", "staff@test.com");
        Environment.SetEnvironmentVariable("CreditCardPassword", "TestPassword1!");
        Environment.SetEnvironmentVariable("TenantURL", string.Empty);
        try
        {
            var (controller, _, _) = CreateController();

            var result = await controller.CreditCard();

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("Home", redirect.ControllerName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CreditCardEmail", null);
            Environment.SetEnvironmentVariable("CreditCardPassword", null);
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    [Fact]
    public async Task CreditCard_ValidLogin_WithTenantUrl_RedirectsToBooking()
    {
        Environment.SetEnvironmentVariable("CreditCardEmail", "staff@test.com");
        Environment.SetEnvironmentVariable("CreditCardPassword", "TestPassword1!");
        Environment.SetEnvironmentVariable("TenantURL", "https://app_name.example.com");
        try
        {
            var (controller, _, _) = CreateController();

            var result = await controller.CreditCard();

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Contains("booking", redirect.Url);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CreditCardEmail", null);
            Environment.SetEnvironmentVariable("CreditCardPassword", null);
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    [Fact]
    public async Task CreditCard_LegacyHash_UpgradesPassword()
    {
        Environment.SetEnvironmentVariable("CreditCardEmail", "legacy@test.com");
        Environment.SetEnvironmentVariable("CreditCardPassword", "LegacyPass1!");
        Environment.SetEnvironmentVariable("TenantURL", string.Empty);
        try
        {
            var (controller, masterCtx, _) = CreateController();

            await controller.CreditCard();

            var user = (await masterCtx.Users.FindAsync([3], TestContext.Current.CancellationToken))!;
            Assert.False(user.IsLegacyHash);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CreditCardEmail", null);
            Environment.SetEnvironmentVariable("CreditCardPassword", null);
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    // ResetPassword POST edge cases
    [Fact]
    public async Task ResetPassword_Post_ValidCode_DespatchUserNotFound_ReturnsView()
    {
        var (controller, _, _) = CreateController();
        // reset@test.com has valid-reset-key but no despatch contact (not in seed data)
        var model = new ResetPasswordViewModel
        {
            Email = "reset@test.com", Password = "NewStrong1!", ConfirmPassword = "NewStrong1!", Code = "valid-reset-key"
        };

        var result = await controller.ResetPassword(model);

        Assert.IsType<ViewResult>(result);
    }

    // ForgotPassword POST - despatch user not found
    [Fact]
    public async Task ForgotPassword_Post_DespatchUserNotFound_ReturnsError()
    {
        var (controller, masterCtx, _) = CreateController();
        masterCtx.Users.Add(new User
        {
            UserId = 32, Email = "nodespatch2@test.com",
            Password = "x", Salt = "x",
            CurrentTenantId = 1, IsLegacyHash = false, IsCourier = false
        });
        masterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 32, TenantId = 1, UserId = 32 });
        await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
        SetFormValues(controller, "token");
        var model = new ForgotPasswordViewModel { Email = "nodespatch2@test.com" };

        var result = await controller.ForgotPassword(model) as JsonResult;

        Assert.NotNull(result);
        AssertHelper.JsonEquivalent(
            new { success = false, message = "Reset failed due to contact validation failure" }, result.Value);
    }

    // GenerateApiKey exception catch block
    [Fact]
    public async Task GenerateApiKey_CryptoException_ReturnsFailure()
    {
        Environment.SetEnvironmentVariable("JWTSecretKey", "ThisIsASecretKeyForTestingThatMustBeLongEnough123!");
        Environment.SetEnvironmentVariable("ClaimsKey", "not-valid-base64!!!");
        Environment.SetEnvironmentVariable("Issuer", "test-issuer");
        Environment.SetEnvironmentVariable("Audience", "test-audience");
        try
        {
            var user = ClaimsPrincipalFactory.Create(email: "staff@test.com");
            var (controller, _, _) = CreateController(user);

            var result = await controller.GenerateApiKey() as JsonResult;

            Assert.NotNull(result);
            AssertHelper.JsonEquivalent(new { success = false, message = "Failed to generate API key" }, result.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JWTSecretKey", null);
            Environment.SetEnvironmentVariable("ClaimsKey", null);
            Environment.SetEnvironmentVariable("Issuer", null);
            Environment.SetEnvironmentVariable("Audience", null);
        }
    }

    // Login - asure redirect
    [Fact]
    public async Task Login_Post_AsureUser_WithTenantUrl_RedirectsToBooking()
    {
        Environment.SetEnvironmentVariable("TenantURL", "https://app_name.example.com");
        try
        {
            var (controller, masterCtx, despatchCtx) = CreateController();
            // Add "urgent" tenant
            masterCtx.Tenants.Add(new Tenant
            {
                TenantId = 3, Name = "Urgent", Dbconnection = "Server=test;Database=TestDB;",
                Code = "urgent", CountryCode = "NZ", TimeZone = "New Zealand Standard Time"
            });
            var asureUser = new User
            {
                UserId = 40, Email = "asure@urgent.co.nz",
                Password = PasswordHelper.HashPassword("AsurePass1!", "77777"), Salt = "77777",
                CurrentTenantId = 3, IsLegacyHash = false, IsCourier = false
            };
            masterCtx.Users.Add(asureUser);
            masterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 40, TenantId = 3, UserId = 40 });
            await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
            // Add despatch contact for asure user
            despatchCtx.TucClientContacts.Add(new TucClientContact
            {
                UcctId = 40, UcctClientId = 1, UserName = "asure@urgent.co.nz",
                UcctFirstname = "Asure", UcctSurname = "User", Active = true,
                HasEmail = true, ValidatedEmail = true,
                Created = DateTime.Now, CreatedBy = "test", LastModified = DateTime.Now, LastModifiedBy = "test"
            });
            await despatchCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

            var model = new LoginViewModel
                { Email = "asure@urgent.co.nz", Password = "AsurePass1!", IsCourierLogin = false };

            var result = await controller.Login(model, null!);

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Contains("booking", redirect.Url);
            Assert.Contains("asure", redirect.Url);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    [Fact]
    public async Task Login_Post_AsureUser_NoTenantUrl_RedirectsToHome()
    {
        Environment.SetEnvironmentVariable("TenantURL", string.Empty);
        try
        {
            var (controller, masterCtx, despatchCtx) = CreateController();
            masterCtx.Tenants.Add(new Tenant
            {
                TenantId = 3, Name = "Urgent", Dbconnection = "Server=test;Database=TestDB;",
                Code = "urgent", CountryCode = "NZ", TimeZone = "New Zealand Standard Time"
            });
            masterCtx.Users.Add(new User
            {
                UserId = 41, Email = "asure@urgent.co.nz",
                Password = PasswordHelper.HashPassword("AsurePass1!", "77778"), Salt = "77778",
                CurrentTenantId = 3, IsLegacyHash = false, IsCourier = false
            });
            masterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 41, TenantId = 3, UserId = 41 });
            await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
            despatchCtx.TucClientContacts.Add(new TucClientContact
            {
                UcctId = 41, UcctClientId = 1, UserName = "asure@urgent.co.nz",
                UcctFirstname = "Asure", UcctSurname = "User2", Active = true,
                HasEmail = true, ValidatedEmail = true,
                Created = DateTime.Now, CreatedBy = "test", LastModified = DateTime.Now, LastModifiedBy = "test"
            });
            await despatchCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

            var model = new LoginViewModel
                { Email = "asure@urgent.co.nz", Password = "AsurePass1!", IsCourierLogin = false };

            var result = await controller.Login(model, null!);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    // ResetPassword - asure redirect
    [Fact]
    public async Task ResetPassword_Post_AsureUser_WithTenantUrl_RedirectsToBooking()
    {
        Environment.SetEnvironmentVariable("TenantURL", "https://app_name.example.com");
        try
        {
            var (controller, masterCtx, despatchCtx) = CreateController();
            masterCtx.Tenants.Add(new Tenant
            {
                TenantId = 3, Name = "Urgent", Dbconnection = "Server=test;Database=TestDB;",
                Code = "urgent", CountryCode = "NZ", TimeZone = "New Zealand Standard Time"
            });
            masterCtx.Users.Add(new User
            {
                UserId = 42, Email = "asure@urgent.co.nz",
                Password = "OLD", Salt = "77779",
                ResetKey = "asure-reset-key",
                CurrentTenantId = 3, IsLegacyHash = false, IsCourier = false
            });
            masterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 42, TenantId = 3, UserId = 42 });
            await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
            despatchCtx.TucClientContacts.Add(new TucClientContact
            {
                UcctId = 42, UcctClientId = 1, UserName = "asure@urgent.co.nz",
                UcctFirstname = "Asure", UcctSurname = "Reset", Active = true,
                HasEmail = true, ValidatedEmail = true,
                Created = DateTime.Now, CreatedBy = "test", LastModified = DateTime.Now, LastModifiedBy = "test"
            });
            await despatchCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

            var model = new ResetPasswordViewModel
            {
                Email = "asure@urgent.co.nz", Password = "NewStrong1!",
                ConfirmPassword = "NewStrong1!", Code = "asure-reset-key"
            };

            var result = await controller.ResetPassword(model);

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Contains("booking", redirect.Url);
            Assert.Contains("asure", redirect.Url);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    [Fact]
    public async Task ResetPassword_Post_AsureUser_NoTenantUrl_RedirectsToHome()
    {
        Environment.SetEnvironmentVariable("TenantURL", string.Empty);
        try
        {
            var (controller, masterCtx, despatchCtx) = CreateController();
            masterCtx.Tenants.Add(new Tenant
            {
                TenantId = 3, Name = "Urgent", Dbconnection = "Server=test;Database=TestDB;",
                Code = "urgent", CountryCode = "NZ", TimeZone = "New Zealand Standard Time"
            });
            masterCtx.Users.Add(new User
            {
                UserId = 43, Email = "asure@urgent.co.nz",
                Password = "OLD", Salt = "77780",
                ResetKey = "asure-reset-key-2",
                CurrentTenantId = 3, IsLegacyHash = false, IsCourier = false
            });
            masterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 43, TenantId = 3, UserId = 43 });
            await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
            despatchCtx.TucClientContacts.Add(new TucClientContact
            {
                UcctId = 43, UcctClientId = 1, UserName = "asure@urgent.co.nz",
                UcctFirstname = "Asure", UcctSurname = "Reset2", Active = true,
                HasEmail = true, ValidatedEmail = true,
                Created = DateTime.Now, CreatedBy = "test", LastModified = DateTime.Now, LastModifiedBy = "test"
            });
            await despatchCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

            var model = new ResetPasswordViewModel
            {
                Email = "asure@urgent.co.nz", Password = "NewStrong1!",
                ConfirmPassword = "NewStrong1!", Code = "asure-reset-key-2"
            };

            var result = await controller.ResetPassword(model);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    // UpdateCurrentTenant - CurrentTenant null after update
    [Fact]
    public async Task UpdateCurrentTenant_TenantNotInDb_ReturnsFailure()
    {
        var user = ClaimsPrincipalFactory.Create(userId: 1, email: "staff@test.com");
        var (controller, masterCtx, _) = CreateController(user);
        // Add tenant-user link for non-existent tenant
        masterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 40, TenantId = 999, UserId = 1 });
        await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 999 }) as JsonResult;

        AssertHelper.JsonEquivalent(
            new { success = false, message = "Current Tenant Not Set for user 1" }, result!.Value);
    }

    // UpdateCurrentTenant - Despatch user not found
    [Fact]
    public async Task UpdateCurrentTenant_DespatchUserNotFound_ReturnsFailure()
    {
        var user = ClaimsPrincipalFactory.Create(userId: 1, email: "nodespatch@test.com");
        var (controller, _, _) = CreateController(user);

        var result = await controller.UpdateCurrentTenant(new TenantUpdateModel { TenantId = 2 }) as JsonResult;

        AssertHelper.JsonEquivalent(
            new { success = false, message = "Despatch User not found" }, result!.Value);
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

    // AcceptTenantSwitchToken tests
    private const string TestClaimsKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32-byte base64

    private static string EncryptTenantSwitchToken(int userId, int tenantId, long? expiresAt = null)
    {
        var token = JsonSerializer.Serialize(new
        {
            UserId = userId,
            TenantId = tenantId,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds()
        });
        using var aes = Aes.Create();
        aes.Key = Convert.FromBase64String(TestClaimsKey);
        aes.GenerateIV();
        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);
        using (var encryptor = aes.CreateEncryptor())
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
            sw.Write(token);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static (AccountController controller, MasterContext masterCtx) CreateForAccept(string host = "hub.test.deliverdifferent.com")
    {
        var (controller, masterCtx, _) = CreateController(ClaimsPrincipalFactory.CreateAnonymous());
        controller.HttpContext.Request.Host = new HostString(host);
        return (controller, masterCtx);
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_EmptyToken_RedirectsToLogin()
    {
        var (controller, _) = CreateForAccept();

        var result = await controller.AcceptTenantSwitchToken("");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_WhitespaceToken_RedirectsToLogin()
    {
        var (controller, _) = CreateForAccept();

        var result = await controller.AcceptTenantSwitchToken("   ");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_GarbageCiphertext_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("ClaimsKey", TestClaimsKey);
        try
        {
            var (controller, _) = CreateForAccept();

            var result = await controller.AcceptTenantSwitchToken("not-base64!@#");

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_ZeroUserId_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("ClaimsKey", TestClaimsKey);
        try
        {
            var (controller, _) = CreateForAccept();
            var token = EncryptTenantSwitchToken(userId: 0, tenantId: 1);

            var result = await controller.AcceptTenantSwitchToken(token);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_ZeroTenantId_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("ClaimsKey", TestClaimsKey);
        try
        {
            var (controller, _) = CreateForAccept();
            var token = EncryptTenantSwitchToken(userId: 1, tenantId: 0);

            var result = await controller.AcceptTenantSwitchToken(token);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_ExpiredToken_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("ClaimsKey", TestClaimsKey);
        try
        {
            var (controller, _) = CreateForAccept();
            var expired = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
            var token = EncryptTenantSwitchToken(userId: 1, tenantId: 1, expiresAt: expired);

            var result = await controller.AcceptTenantSwitchToken(token);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_UserNotFound_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("ClaimsKey", TestClaimsKey);
        try
        {
            var (controller, _) = CreateForAccept();
            var token = EncryptTenantSwitchToken(userId: 9999, tenantId: 1);

            var result = await controller.AcceptTenantSwitchToken(token);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_HostDoesNotExposeTenant_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("ClaimsKey", TestClaimsKey);
        try
        {
            // localhost has no tenant segment -> ExtractTenantFromHost returns null
            var (controller, _) = CreateForAccept(host: "localhost");
            var token = EncryptTenantSwitchToken(userId: 1, tenantId: 1);

            var result = await controller.AcceptTenantSwitchToken(token);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_HostTenantMismatch_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("ClaimsKey", TestClaimsKey);
        try
        {
            // user 1's current tenant code is "test"; host says "second"
            var (controller, _) = CreateForAccept(host: "hub.second.deliverdifferent.com");
            var token = EncryptTenantSwitchToken(userId: 1, tenantId: 1);

            var result = await controller.AcceptTenantSwitchToken(token);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_TokenTenantMismatch_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("ClaimsKey", TestClaimsKey);
        try
        {
            // host matches "test" and so does the user's current tenant, but the token claims tenant 2
            var (controller, _) = CreateForAccept(host: "hub.test.deliverdifferent.com");
            var token = EncryptTenantSwitchToken(userId: 1, tenantId: 2);

            var result = await controller.AcceptTenantSwitchToken(token);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_DespatchUserNotFound_RedirectsToLogin()
    {
        Environment.SetEnvironmentVariable("ClaimsKey", TestClaimsKey);
        try
        {
            var (controller, masterCtx) = CreateForAccept();
            masterCtx.Users.Add(new User
            {
                UserId = 50, Email = "ghost@test.com",
                Password = "x", Salt = "x", CurrentTenantId = 1, IsLegacyHash = false, IsCourier = false
            });
            masterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 50, TenantId = 1, UserId = 50 });
            await masterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

            var token = EncryptTenantSwitchToken(userId: 50, tenantId: 1);

            var result = await controller.AcceptTenantSwitchToken(token);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_ValidToken_RedirectsToHomeIndex()
    {
        Environment.SetEnvironmentVariable("ClaimsKey", TestClaimsKey);
        try
        {
            var (controller, _) = CreateForAccept(host: "hub.test.deliverdifferent.com");
            var token = EncryptTenantSwitchToken(userId: 1, tenantId: 1);

            var result = await controller.AcceptTenantSwitchToken(token);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("Home", redirect.ControllerName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }
}
