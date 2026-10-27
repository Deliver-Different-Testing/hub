using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Hub.Interfaces;
using Hub.Models;
using Hub.Models.Master;
using Hub.Repositories;
using Hub.Services;
using Hub.Shared;
using Hub.Tests.Helpers;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Hub.Tests.Services;

[Collection("EnvironmentVariables")]
public class AccountServiceTests : IDisposable
{
    private readonly string _originalCredentials;
    private readonly string _originalRecaptchaKey;

    public AccountServiceTests()
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

    private sealed record Harness(
        AccountService Service,
        DefaultHttpContext Http,
        MasterContext MasterCtx,
        DynamicDespatchDbContext DespatchCtx);

    private static Harness Create(ClaimsPrincipal? user = null, HttpClient? httpClient = null)
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

        var service = new AccountService(connectionStringManager, despatchRepo, authRepo, httpClient);
        var http = ControllerTestBase.CreateHttpContext(user ?? ClaimsPrincipalFactory.CreateAnonymous());

        return new Harness(service, http, masterCtx, despatchCtx);
    }

    private static LoginViewModel Login(string email, string password, bool courier = false) =>
        new() { Email = email, Password = password, IsCourierLogin = courier };

    private static ResetPasswordViewModel Reset(string code, string password = "NewStrong1!") =>
        new() { Password = password, ConfirmPassword = password, Code = code };

    private static async Task AddContactAsync(DynamicDespatchDbContext ctx, int id, string email, string surname)
    {
        ctx.TucClientContacts.Add(new TucClientContact
        {
            UcctId = id, UcctClientId = 1, UserName = email, UcctFirstname = "Test", UcctSurname = surname,
            Active = true, HasEmail = true, ValidatedEmail = true,
            Created = DateTime.Now, CreatedBy = "test", LastModified = DateTime.Now, LastModifiedBy = "test"
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task AddUrgentTenantAsync(MasterContext ctx)
    {
        ctx.Tenants.Add(new Tenant
        {
            TenantId = 3, Name = "Urgent", Dbconnection = "Server=test;Database=TestDB;",
            Code = "urgent", CountryCode = "NZ", TimeZone = "New Zealand Standard Time"
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static IAuthenticationService AuthService(HttpContext http) =>
        (IAuthenticationService)http.RequestServices.GetService(typeof(IAuthenticationService))!;

    // ---- Login ----

    [Fact]
    public async Task Login_UserNotFound_ReturnsErrorNamingTheLoginType()
    {
        var h = Create();

        var staff = await h.Service.LoginAsync(h.Http, Login("nobody@test.com", "pass"), null);
        var courier = await h.Service.LoginAsync(h.Http, Login("nobody@test.com", "pass", courier: true), null);

        Assert.False(staff.Succeeded);
        Assert.Equal("Invalid login attempt. No Staff Login account found for this email.", staff.Error);
        Assert.Equal("Invalid login attempt. No Courier Login account found for this email.", courier.Error);
    }

    [Fact]
    public async Task Login_NullTenant_ReturnsError()
    {
        var h = Create();
        h.MasterCtx.Users.Add(new User
        {
            UserId = 10, Email = "notenant@test.com", Password = "x", Salt = "x",
            CurrentTenantId = null, IsLegacyHash = false, IsCourier = false
        });
        await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await h.Service.LoginAsync(h.Http, Login("notenant@test.com", "pass"), null);

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid login attempt.", result.Error);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsError()
    {
        var h = Create();

        var result = await h.Service.LoginAsync(h.Http, Login("staff@test.com", "WrongPassword!"), null);

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid login attempt.", result.Error);
    }

    [Fact]
    public async Task Login_ValidStaffLogin_RedirectsToHome()
    {
        var h = Create();

        var result = await h.Service.LoginAsync(h.Http, Login("staff@test.com", "TestPassword1!"), null);

        Assert.True(result.Succeeded);
        Assert.Equal("/", result.RedirectUrl);
    }

    [Fact]
    public async Task Login_ValidStaffLogin_WithLocalReturnUrl_ReturnsThere()
    {
        var h = Create();

        var result = await h.Service.LoginAsync(
            h.Http, Login("staff@test.com", "TestPassword1!"), "/connect/authorize?client_id=dfrnt-claude&state=abc");

        Assert.True(result.Succeeded);
        Assert.Equal("/connect/authorize?client_id=dfrnt-claude&state=abc", result.RedirectUrl);
    }

    [Fact]
    public async Task Login_ValidCourierLogin_IgnoresReturnUrl()
    {
        var h = Create();

        var result = await h.Service.LoginAsync(
            h.Http, Login("courier@test.com", "CourierPass1!", courier: true), "/connect/authorize?client_id=x");

        Assert.True(result.Succeeded);
        Assert.Equal("/", result.RedirectUrl);
    }

    [Fact]
    public async Task Login_ValidCourierLogin_RedirectsToHome()
    {
        var h = Create();

        var result = await h.Service.LoginAsync(h.Http, Login("courier@test.com", "CourierPass1!", courier: true), null);

        Assert.True(result.Succeeded);
        Assert.Equal("/", result.RedirectUrl);
    }

    [Fact]
    public async Task Login_CourierLogin_IssuesCourierClaims()
    {
        var h = Create();

        await h.Service.LoginAsync(h.Http, Login("courier@test.com", "CourierPass1!", courier: true), null);

        await AuthService(h.Http).Received(1).SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Is<ClaimsPrincipal>(p =>
                p.HasClaim("IsCourier", "True") &&
                p.HasClaim("ContactID", "0") &&
                p.HasClaim("ClientID", "0") &&
                p.HasClaim("StaffID", "") &&
                p.HasClaim("Internal", "False")),
            Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public async Task Login_NetworkPartner_IssuesIsNetworkPartnerClaim()
    {
        var h = Create();

        var result = await h.Service.LoginAsync(h.Http, Login("np@test.com", "NpPass1!"), null);

        Assert.True(result.Succeeded);
        await AuthService(h.Http).Received(1).SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Is<ClaimsPrincipal>(p =>
                p.HasClaim("IsNetworkPartner", "True") &&
                p.HasClaim("IsCourier", "False") &&
                // Data-scope claims: NP contact sits on the NetworkPartner
                // client (ClientType 3, NpAgentId 777) seeded in the factory.
                p.HasClaim("ClientTypeId", "3") &&
                p.HasClaim("NpAgentId", "777") &&
                // Unified Permissions §5 — RoleId is a transitional dual-write
                // of the same value as NpRoleId (NpAdmin = 1 from the factory).
                p.HasClaim("NpRoleId", "1") &&
                p.HasClaim("RoleId", "1")),
            Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public async Task Login_StaffLogin_IsNetworkPartnerClaimIsFalse()
    {
        var h = Create();

        var result = await h.Service.LoginAsync(h.Http, Login("staff@test.com", "TestPassword1!"), null);

        Assert.True(result.Succeeded);
        await AuthService(h.Http).Received(1).SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Is<ClaimsPrincipal>(p =>
                p.HasClaim("IsNetworkPartner", "False") &&
                // staff@test.com sits on the Customer client (ClientType 2,
                // no NpAgentId) — the data-scope claims reflect that.
                p.HasClaim("ClientTypeId", "2") &&
                p.HasClaim("NpAgentId", "") &&
                // Unified Permissions §5 — staff contact has no role; RoleId
                // dual-writes the same empty value as NpRoleId.
                p.HasClaim("RoleId", "")),
            Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public async Task Login_RememberMe_SignsInPersistentWithSevenDayExpiry()
    {
        var h = Create();
        var model = new LoginViewModel { Email = "staff@test.com", Password = "TestPassword1!", RememberMe = true };

        await h.Service.LoginAsync(h.Http, model, null);

        await AuthService(h.Http).Received(1).SignInAsync(
            Arg.Any<HttpContext>(),
            "Identity.Application",
            Arg.Any<ClaimsPrincipal>(),
            Arg.Is<AuthenticationProperties>(a => a.IsPersistent && a.ExpiresUtc.HasValue && a.AllowRefresh == true));
    }

    [Fact]
    public async Task Login_WithoutRememberMe_SignsInSessionCookie()
    {
        var h = Create();

        await h.Service.LoginAsync(h.Http, Login("staff@test.com", "TestPassword1!"), null);

        await AuthService(h.Http).Received(1).SignInAsync(
            Arg.Any<HttpContext>(),
            "Identity.Application",
            Arg.Any<ClaimsPrincipal>(),
            Arg.Is<AuthenticationProperties>(a => !a.IsPersistent && !a.ExpiresUtc.HasValue));
    }

    [Fact]
    public async Task Login_LegacyHashUpgrade_UpdatesPassword()
    {
        var h = Create();

        await h.Service.LoginAsync(h.Http, Login("legacy@test.com", "LegacyPass1!"), null);

        var user = (await h.MasterCtx.Users.FindAsync([3], TestContext.Current.CancellationToken))!;
        Assert.False(user.IsLegacyHash);
        // Password should now be the modern hash
        var expectedHash = PasswordHelper.HashPassword("LegacyPass1!", "11111");
        Assert.Equal(expectedHash, user.Password);
    }

    [Fact]
    public async Task Login_CourierNotInDespatchDb_ReturnsError()
    {
        var h = Create();
        // Create courier user whose email doesn't exist in despatch DB
        h.MasterCtx.Users.Add(new User
        {
            UserId = 20, Email = "ghost-courier@test.com",
            Password = PasswordHelper.HashPassword("Pass1!", "33333"), Salt = "33333",
            CurrentTenantId = 1, IsLegacyHash = false, IsCourier = true
        });
        h.MasterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 20, TenantId = 1, UserId = 20 });
        await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await h.Service.LoginAsync(h.Http, Login("ghost-courier@test.com", "Pass1!", courier: true), null);

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid login attempt. Courier account not properly configured.", result.Error);
    }

    [Fact]
    public async Task Login_StaffNotInDespatchDb_ReturnsError()
    {
        var h = Create();
        h.MasterCtx.Users.Add(new User
        {
            UserId = 21, Email = "ghost-staff@test.com",
            Password = PasswordHelper.HashPassword("Pass1!", "44444"), Salt = "44444",
            CurrentTenantId = 1, IsLegacyHash = false, IsCourier = false
        });
        h.MasterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 21, TenantId = 1, UserId = 21 });
        await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await h.Service.LoginAsync(h.Http, Login("ghost-staff@test.com", "Pass1!"), null);

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid login attempt.", result.Error);
    }

    [Fact]
    public async Task Login_AlreadyAuthenticatedAsDifferentUser_ReturnsError()
    {
        var h = Create(ClaimsPrincipalFactory.Create(email: "other@test.com"));

        var result = await h.Service.LoginAsync(h.Http, Login("staff@test.com", "TestPassword1!"), null);

        Assert.False(result.Succeeded);
        Assert.Contains("other@test.com", result.Error);
        Assert.Contains("logout first", result.Error);
        await AuthService(h.Http).DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task Login_AlreadyAuthenticatedAsSameUser_ProceedsWithLogin()
    {
        var h = Create(ClaimsPrincipalFactory.Create(email: "staff@test.com"));

        var result = await h.Service.LoginAsync(h.Http, Login("staff@test.com", "TestPassword1!"), null);

        Assert.True(result.Succeeded);
        Assert.Equal("/", result.RedirectUrl);
    }

    [Fact]
    public async Task Login_MissingSQLCredentials_Throws()
    {
        var original = Environment.GetEnvironmentVariable("SQLCredentials");
        Environment.SetEnvironmentVariable("SQLCredentials", "");
        try
        {
            var h = Create();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                h.Service.LoginAsync(h.Http, Login("staff@test.com", "TestPassword1!"), null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SQLCredentials", original);
        }
    }

    [Fact]
    public async Task Login_AsureUser_WithTenantUrl_RedirectsToBooking()
    {
        Environment.SetEnvironmentVariable("TenantURL", "https://app_name.example.com");
        try
        {
            var h = Create();
            await AddUrgentTenantAsync(h.MasterCtx);
            h.MasterCtx.Users.Add(new User
            {
                UserId = 40, Email = "asure@urgent.co.nz",
                Password = PasswordHelper.HashPassword("AsurePass1!", "77777"), Salt = "77777",
                CurrentTenantId = 3, IsLegacyHash = false, IsCourier = false
            });
            h.MasterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 40, TenantId = 3, UserId = 40 });
            await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
            await AddContactAsync(h.DespatchCtx, 40, "asure@urgent.co.nz", "User");

            var result = await h.Service.LoginAsync(h.Http, Login("asure@urgent.co.nz", "AsurePass1!"), null);

            Assert.True(result.Succeeded);
            Assert.Equal("https://booking.example.com/#/asure", result.RedirectUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    [Fact]
    public async Task Login_AsureUser_NoTenantUrl_RedirectsToHome()
    {
        Environment.SetEnvironmentVariable("TenantURL", string.Empty);
        try
        {
            var h = Create();
            await AddUrgentTenantAsync(h.MasterCtx);
            h.MasterCtx.Users.Add(new User
            {
                UserId = 41, Email = "asure@urgent.co.nz",
                Password = PasswordHelper.HashPassword("AsurePass1!", "77778"), Salt = "77778",
                CurrentTenantId = 3, IsLegacyHash = false, IsCourier = false
            });
            h.MasterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 41, TenantId = 3, UserId = 41 });
            await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
            await AddContactAsync(h.DespatchCtx, 41, "asure@urgent.co.nz", "User2");

            var result = await h.Service.LoginAsync(h.Http, Login("asure@urgent.co.nz", "AsurePass1!"), null);

            Assert.True(result.Succeeded);
            Assert.Equal("/", result.RedirectUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    // ---- CreditCard ----

    private static async Task<AuthResult> CreditCardAsync(
        Harness h, string? email, string? password, string? tenantUrl = null)
    {
        Environment.SetEnvironmentVariable("CreditCardEmail", email);
        Environment.SetEnvironmentVariable("CreditCardPassword", password);
        Environment.SetEnvironmentVariable("TenantURL", tenantUrl);
        try
        {
            return await h.Service.CreditCardLoginAsync(h.Http);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CreditCardEmail", null);
            Environment.SetEnvironmentVariable("CreditCardPassword", null);
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    [Fact]
    public async Task CreditCard_MissingCredentials_ReturnsNotConfigured()
    {
        var result = await CreditCardAsync(Create(), string.Empty, string.Empty);

        Assert.False(result.Succeeded);
        Assert.Equal("Auto-login not configured.", result.Error);
    }

    [Fact]
    public async Task CreditCard_UserNotFound_ReturnsManualLoginError()
    {
        var result = await CreditCardAsync(Create(), "nobody@test.com", "pass");

        Assert.False(result.Succeeded);
        Assert.Equal("Auto-login failed. Please login manually.", result.Error);
    }

    [Fact]
    public async Task CreditCard_NullTenant_ReturnsManualLoginError()
    {
        var h = Create();
        h.MasterCtx.Users.Add(new User
        {
            UserId = 30, Email = "notenant@test.com",
            Password = PasswordHelper.HashPassword("Pass1!", "99999"), Salt = "99999",
            CurrentTenantId = null, IsLegacyHash = false, IsCourier = false
        });
        await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreditCardAsync(h, "notenant@test.com", "Pass1!");

        Assert.False(result.Succeeded);
        Assert.Equal("Auto-login failed. Please login manually.", result.Error);
    }

    [Fact]
    public async Task CreditCard_WrongPassword_ReturnsManualLoginError()
    {
        var result = await CreditCardAsync(Create(), "staff@test.com", "WrongPassword!");

        Assert.False(result.Succeeded);
        Assert.Equal("Auto-login failed. Please login manually.", result.Error);
    }

    [Fact]
    public async Task CreditCard_DespatchUserNotFound_ReturnsManualLoginError()
    {
        var h = Create();
        h.MasterCtx.Users.Add(new User
        {
            UserId = 31, Email = "nodespatch@test.com",
            Password = PasswordHelper.HashPassword("Pass1!", "88888"), Salt = "88888",
            CurrentTenantId = 1, IsLegacyHash = false, IsCourier = false
        });
        h.MasterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 31, TenantId = 1, UserId = 31 });
        await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreditCardAsync(h, "nodespatch@test.com", "Pass1!");

        Assert.False(result.Succeeded);
        Assert.Equal("Auto-login failed. Please login manually.", result.Error);
    }

    [Fact]
    public async Task CreditCard_ValidLogin_NoTenantUrl_RedirectsToHome()
    {
        var h = Create();

        var result = await CreditCardAsync(h, "staff@test.com", "TestPassword1!", tenantUrl: string.Empty);

        Assert.True(result.Succeeded);
        Assert.Equal("/", result.RedirectUrl);
        await AuthService(h.Http).Received(1).SignInAsync(
            Arg.Any<HttpContext>(), "Identity.Application", Arg.Any<ClaimsPrincipal>(),
            Arg.Is<AuthenticationProperties>(a => !a.IsPersistent));
    }

    [Fact]
    public async Task CreditCard_ValidLogin_WithTenantUrl_RedirectsToBooking()
    {
        var result = await CreditCardAsync(
            Create(), "staff@test.com", "TestPassword1!", tenantUrl: "https://app_name.example.com");

        Assert.True(result.Succeeded);
        Assert.Equal("https://booking.example.com", result.RedirectUrl);
    }

    [Fact]
    public async Task CreditCard_LegacyHash_UpgradesPassword()
    {
        var h = Create();

        await CreditCardAsync(h, "legacy@test.com", "LegacyPass1!", tenantUrl: string.Empty);

        var user = (await h.MasterCtx.Users.FindAsync([3], TestContext.Current.CancellationToken))!;
        Assert.False(user.IsLegacyHash);
    }

    // ---- Reset password ----

    [Fact]
    public async Task GetResetEmail_NullOrEmptyCode_ReturnsNull()
    {
        var h = Create();

        Assert.Null(await h.Service.GetResetEmailAsync(null));
        Assert.Null(await h.Service.GetResetEmailAsync(string.Empty));
    }

    [Fact]
    public async Task GetResetEmail_InvalidCode_ReturnsNull()
    {
        var h = Create();

        Assert.Null(await h.Service.GetResetEmailAsync("invalid-code"));
    }

    [Fact]
    public async Task GetResetEmail_ValidCode_ReturnsEmail()
    {
        var h = Create();

        Assert.Equal("reset@test.com", await h.Service.GetResetEmailAsync("valid-reset-key"));
    }

    [Fact]
    public async Task ResetPassword_InvalidCode_ReturnsError()
    {
        var h = Create();

        var result = await h.Service.ResetPasswordAsync(h.Http, Reset("bad-code"));

        Assert.False(result.Succeeded);
        Assert.Equal("This reset link is invalid or has expired.", result.Error);
    }

    [Fact]
    public async Task ResetPassword_ValidReset_UpdatesPasswordClearsKeyAndSignsIn()
    {
        var h = Create();
        // Add the reset user's contact to despatch DB so full flow works
        await AddContactAsync(h.DespatchCtx, 10, "reset@test.com", "User");

        var result = await h.Service.ResetPasswordAsync(h.Http, Reset("valid-reset-key"));

        Assert.True(result.Succeeded);
        Assert.Equal("/", result.RedirectUrl);
        var user = (await h.MasterCtx.Users.FindAsync([4], TestContext.Current.CancellationToken))!;
        Assert.Null(user.ResetKey);
        Assert.NotEqual("RESETPASSWORD", user.Password);
        await AuthService(h.Http).Received(1).SignInAsync(
            Arg.Any<HttpContext>(), "Identity.Application",
            Arg.Is<ClaimsPrincipal>(p => p.HasClaim(ClaimTypes.Name, "reset@test.com")),
            Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public async Task ResetPassword_LegacyHashUser_ClearsFlagAndNewPasswordVerifies()
    {
        var h = Create();
        await AddContactAsync(h.DespatchCtx, 10, "reset@test.com", "User");
        var legacyUser = (await h.MasterCtx.Users.FindAsync([4], TestContext.Current.CancellationToken))!;
        legacyUser.IsLegacyHash = true;
        await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

        await h.Service.ResetPasswordAsync(h.Http, Reset("valid-reset-key"));

        var user = (await h.MasterCtx.Users.FindAsync([4], TestContext.Current.CancellationToken))!;
        Assert.False(user.IsLegacyHash);
        Assert.True(PasswordHelper.Verify("NewStrong1!", user.Salt, user.Password, user.IsLegacyHash));
    }

    [Fact]
    public async Task ResetPassword_ValidCode_DespatchUserNotFound_ReportsPasswordResetButNoSignIn()
    {
        var h = Create();
        // reset@test.com has valid-reset-key but no despatch contact (not in seed data)

        var result = await h.Service.ResetPasswordAsync(h.Http, Reset("valid-reset-key"));

        Assert.False(result.Succeeded);
        Assert.Contains("password was reset", result.Error);
        var user = (await h.MasterCtx.Users.FindAsync([4], TestContext.Current.CancellationToken))!;
        Assert.Null(user.ResetKey);
        await AuthService(h.Http).DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task ResetPassword_AsureUser_WithTenantUrl_RedirectsToBooking()
    {
        Environment.SetEnvironmentVariable("TenantURL", "https://app_name.example.com");
        try
        {
            var h = Create();
            await AddUrgentTenantAsync(h.MasterCtx);
            h.MasterCtx.Users.Add(new User
            {
                UserId = 42, Email = "asure@urgent.co.nz", Password = "OLD", Salt = "77779",
                ResetKey = "asure-reset-key", CurrentTenantId = 3, IsLegacyHash = false, IsCourier = false
            });
            h.MasterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 42, TenantId = 3, UserId = 42 });
            await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
            await AddContactAsync(h.DespatchCtx, 42, "asure@urgent.co.nz", "Reset");

            var result = await h.Service.ResetPasswordAsync(h.Http, Reset("asure-reset-key"));

            Assert.True(result.Succeeded);
            Assert.Equal("https://booking.example.com/#/asure", result.RedirectUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    [Fact]
    public async Task ResetPassword_AsureUser_NoTenantUrl_RedirectsToHome()
    {
        Environment.SetEnvironmentVariable("TenantURL", string.Empty);
        try
        {
            var h = Create();
            await AddUrgentTenantAsync(h.MasterCtx);
            h.MasterCtx.Users.Add(new User
            {
                UserId = 43, Email = "asure@urgent.co.nz", Password = "OLD", Salt = "77780",
                ResetKey = "asure-reset-key-2", CurrentTenantId = 3, IsLegacyHash = false, IsCourier = false
            });
            h.MasterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 43, TenantId = 3, UserId = 43 });
            await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
            await AddContactAsync(h.DespatchCtx, 43, "asure@urgent.co.nz", "Reset2");

            var result = await h.Service.ResetPasswordAsync(h.Http, Reset("asure-reset-key-2"));

            Assert.True(result.Succeeded);
            Assert.Equal("/", result.RedirectUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TenantURL", null);
        }
    }

    // ---- Forgot password ----

    [Fact]
    public async Task ForgotPassword_RecaptchaFails_ReturnsCaptchaFailedAndDoesNotTouchTheUser()
    {
        var h = Create(httpClient: MockHttpMessageHandler.CreateReCaptchaClient(success: false, score: 0.1));

        var outcome = await h.Service.ForgotPasswordAsync("staff@test.com", "fake-token");

        Assert.Equal(ForgotPasswordOutcome.CaptchaFailed, outcome);
        var user = (await h.MasterCtx.Users.FindAsync([1], TestContext.Current.CancellationToken))!;
        Assert.True(string.IsNullOrEmpty(user.ResetKey));
    }

    [Fact]
    public async Task ForgotPassword_LowRecaptchaScore_ReturnsCaptchaFailed()
    {
        var h = Create(httpClient: MockHttpMessageHandler.CreateReCaptchaClient(success: true, score: 0.4));

        var outcome = await h.Service.ForgotPasswordAsync("staff@test.com", "token");

        Assert.Equal(ForgotPasswordOutcome.CaptchaFailed, outcome);
    }

    [Fact]
    public async Task ForgotPassword_UserNotFound_ReturnsUserNotFound()
    {
        var h = Create();

        var outcome = await h.Service.ForgotPasswordAsync("nobody@test.com", "token");

        Assert.Equal(ForgotPasswordOutcome.UserNotFound, outcome);
    }

    [Fact]
    public async Task ForgotPassword_ValidRequest_SetsResetKey()
    {
        var h = Create();

        var outcome = await h.Service.ForgotPasswordAsync("staff@test.com", "token");

        Assert.Equal(ForgotPasswordOutcome.Sent, outcome);
        var user = (await h.MasterCtx.Users.FindAsync([1], TestContext.Current.CancellationToken))!;
        Assert.False(string.IsNullOrEmpty(user.ResetKey));
    }

    [Fact]
    public async Task ForgotPassword_DespatchUserNotFound_ReturnsContactValidationFailed()
    {
        var h = Create();
        h.MasterCtx.Users.Add(new User
        {
            UserId = 32, Email = "nodespatch2@test.com", Password = "x", Salt = "x",
            CurrentTenantId = 1, IsLegacyHash = false, IsCourier = false
        });
        h.MasterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 32, TenantId = 1, UserId = 32 });
        await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outcome = await h.Service.ForgotPasswordAsync("nodespatch2@test.com", "token");

        Assert.Equal(ForgotPasswordOutcome.ContactValidationFailed, outcome);
    }

    // ---- Logout ----

    [Fact]
    public async Task SignOut_SignsOutAndClearsSession()
    {
        var h = Create(ClaimsPrincipalFactory.Create());

        await h.Service.SignOutAsync(h.Http);

        await AuthService(h.Http).Received(1)
            .SignOutAsync(Arg.Any<HttpContext>(), "Identity.Application", Arg.Any<AuthenticationProperties>());
        h.Http.Session.Received(1).Clear();
    }

    // ---- UpdateCurrentTenant ----

    [Fact]
    public async Task UpdateCurrentTenant_NoClaim_ReturnsFailure()
    {
        var h = Create(ClaimsPrincipalFactory.CreateAnonymous());

        var result = await h.Service.UpdateCurrentTenantAsync(h.Http, 1);

        Assert.False(result.Success);
        Assert.Equal("User not found", result.Message);
    }

    [Fact]
    public async Task UpdateCurrentTenant_ZeroTenantId_ReturnsFailure()
    {
        var h = Create(ClaimsPrincipalFactory.Create());

        var result = await h.Service.UpdateCurrentTenantAsync(h.Http, 0);

        Assert.False(result.Success);
        Assert.Equal("Invalid tenant ID", result.Message);
    }

    [Fact]
    public async Task UpdateCurrentTenant_NotAssociated_ReturnsFailure()
    {
        // User 2 not associated with tenant 2 — blocked at the master-side access gate
        // before any DB write or Despatch lookup is attempted.
        var h = Create(ClaimsPrincipalFactory.Create(userId: 2));

        var result = await h.Service.UpdateCurrentTenantAsync(h.Http, 2);

        Assert.False(result.Success);
        Assert.Equal("You don't have access to that tenant.", result.Message);
    }

    [Fact]
    public async Task UpdateCurrentTenant_ValidUpdate_ReturnsSuccess()
    {
        var h = Create(ClaimsPrincipalFactory.Create(userId: 1, email: "staff@test.com"));

        var result = await h.Service.UpdateCurrentTenantAsync(h.Http, 2);

        // Staff user (userId=1) is associated with both tenants. The test host (localhost)
        // has no cross-host destination, so there is no switch-token URL.
        Assert.True(result.Success);
        Assert.Null(result.RedirectUrl);
        Assert.Equal(2, h.MasterCtx.Users.Find(1)!.CurrentTenantId);
    }

    [Fact]
    public async Task UpdateCurrentTenant_OnRecognisedHost_ReturnsSwitchTokenUrl()
    {
        Environment.SetEnvironmentVariable("ClaimsKey", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        try
        {
            var h = Create(ClaimsPrincipalFactory.Create(userId: 1, email: "staff@test.com"));
            h.Http.Request.Host = new HostString("hub.test.deliverdifferent.com");

            var result = await h.Service.UpdateCurrentTenantAsync(h.Http, 2);

            Assert.True(result.Success);
            Assert.StartsWith("https://hub.second.deliverdifferent.com/Account/AcceptTenantSwitchToken?t=", result.RedirectUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }

    [Fact]
    public async Task UpdateCurrentTenant_TenantHasNoConnection_ReturnsFailureWithoutWriting()
    {
        var h = Create(ClaimsPrincipalFactory.Create(userId: 1, email: "staff@test.com"));
        // Associate the user with a tenant id that has no Tenant row (no Dbconnection).
        h.MasterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 40, TenantId = 999, UserId = 1 });
        await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await h.Service.UpdateCurrentTenantAsync(h.Http, 999);

        Assert.False(result.Success);
        Assert.Equal(
            "That tenant isn't fully configured (no database connection). Contact an administrator.",
            result.Message);
        // Validate-before-write: the bad switch must not have been persisted.
        Assert.Equal(1, h.MasterCtx.Users.Find(1)!.CurrentTenantId);
    }

    // Associated in Master but no Despatch contact in the target tenant. This is the provisioning
    // gap that previously stranded users: the switch must be refused AND CurrentTenant left unchanged.
    [Fact]
    public async Task UpdateCurrentTenant_NoDespatchContact_ReturnsFailureWithoutWriting()
    {
        var h = Create(ClaimsPrincipalFactory.Create(userId: 1, email: "nodespatch@test.com"));

        var result = await h.Service.UpdateCurrentTenantAsync(h.Http, 2);

        Assert.False(result.Success);
        Assert.Equal(
            "Your account isn't set up in that tenant yet — there's no operator record for "
            + "nodespatch@test.com there. Ask an administrator to add you to that tenant before switching.",
            result.Message);
        // The corrupting write that caused the original incident must not happen.
        Assert.Equal(1, h.MasterCtx.Users.Find(1)!.CurrentTenantId);
    }

    // ---- Settings ----

    [Fact]
    public async Task GetSettings_NoClaim_ReturnsEmpty()
    {
        var h = Create(ClaimsPrincipalFactory.CreateAnonymous());

        var result = await h.Service.GetSettingsAsync(h.Http.User);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSettings_UserNotFound_ReturnsEmpty()
    {
        var h = Create(ClaimsPrincipalFactory.Create(email: "nobody@test.com"));

        var result = await h.Service.GetSettingsAsync(h.Http.User);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSettings_ValidUser_ReturnsSettings()
    {
        var h = Create(ClaimsPrincipalFactory.Create(email: "staff@test.com"));

        var result = await h.Service.GetSettingsAsync(h.Http.User);

        Assert.Contains(result, s => s.Name == "Theme");
    }

    // ---- GenerateApiKey ----

    [Fact]
    public async Task GenerateApiKey_MissingClaims_ReturnsFailure()
    {
        var h = Create(ClaimsPrincipalFactory.CreateAnonymous());

        var result = await h.Service.GenerateApiKeyAsync(h.Http.User);

        Assert.False(result.Success);
        Assert.Null(result.ApiKey);
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
            var h = Create(ClaimsPrincipalFactory.Create(email: "staff@test.com"));

            var result = await h.Service.GenerateApiKeyAsync(h.Http.User);

            Assert.True(result.Success);
            Assert.False(string.IsNullOrEmpty(result.ApiKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable("JWTSecretKey", null);
            Environment.SetEnvironmentVariable("ClaimsKey", null);
            Environment.SetEnvironmentVariable("Issuer", null);
            Environment.SetEnvironmentVariable("Audience", null);
        }
    }

    [Fact]
    public async Task GenerateApiKey_CryptoException_ReturnsFailure()
    {
        Environment.SetEnvironmentVariable("JWTSecretKey", "ThisIsASecretKeyForTestingThatMustBeLongEnough123!");
        Environment.SetEnvironmentVariable("ClaimsKey", "not-valid-base64!!!");
        Environment.SetEnvironmentVariable("Issuer", "test-issuer");
        Environment.SetEnvironmentVariable("Audience", "test-audience");
        try
        {
            var h = Create(ClaimsPrincipalFactory.Create(email: "staff@test.com"));

            var result = await h.Service.GenerateApiKeyAsync(h.Http.User);

            Assert.False(result.Success);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JWTSecretKey", null);
            Environment.SetEnvironmentVariable("ClaimsKey", null);
            Environment.SetEnvironmentVariable("Issuer", null);
            Environment.SetEnvironmentVariable("Audience", null);
        }
    }

    // ---- AcceptTenantSwitchToken ----

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

    private static Harness CreateForAccept(string host = "hub.test.deliverdifferent.com")
    {
        var h = Create(ClaimsPrincipalFactory.CreateAnonymous());
        h.Http.Request.Host = new HostString(host);
        return h;
    }

    private static async Task<bool> AcceptAsync(Harness h, string token)
    {
        Environment.SetEnvironmentVariable("ClaimsKey", TestClaimsKey);
        try
        {
            return await h.Service.AcceptTenantSwitchTokenAsync(h.Http, token);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ClaimsKey", null);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AcceptTenantSwitchToken_BlankToken_IsRejected(string? token)
    {
        var h = CreateForAccept();

        Assert.False(await h.Service.AcceptTenantSwitchTokenAsync(h.Http, token));
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_GarbageCiphertext_IsRejected() =>
        Assert.False(await AcceptAsync(CreateForAccept(), "not-base64!@#"));

    [Fact]
    public async Task AcceptTenantSwitchToken_ZeroUserId_IsRejected() =>
        Assert.False(await AcceptAsync(CreateForAccept(), EncryptTenantSwitchToken(userId: 0, tenantId: 1)));

    [Fact]
    public async Task AcceptTenantSwitchToken_ZeroTenantId_IsRejected() =>
        Assert.False(await AcceptAsync(CreateForAccept(), EncryptTenantSwitchToken(userId: 1, tenantId: 0)));

    [Fact]
    public async Task AcceptTenantSwitchToken_ExpiredToken_IsRejected()
    {
        var expired = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();

        Assert.False(await AcceptAsync(CreateForAccept(), EncryptTenantSwitchToken(1, 1, expired)));
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_UserNotFound_IsRejected() =>
        Assert.False(await AcceptAsync(CreateForAccept(), EncryptTenantSwitchToken(userId: 9999, tenantId: 1)));

    [Fact]
    public async Task AcceptTenantSwitchToken_HostDoesNotExposeTenant_IsRejected()
    {
        // localhost has no tenant segment -> ExtractTenantFromHost returns null
        Assert.False(await AcceptAsync(CreateForAccept(host: "localhost"), EncryptTenantSwitchToken(1, 1)));
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_HostTenantMismatch_IsRejected()
    {
        // user 1's current tenant code is "test"; host says "second"
        var h = CreateForAccept(host: "hub.second.deliverdifferent.com");

        Assert.False(await AcceptAsync(h, EncryptTenantSwitchToken(1, 1)));
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_TokenTenantMismatch_IsRejected()
    {
        // host matches "test" and so does the user's current tenant, but the token claims tenant 2
        Assert.False(await AcceptAsync(CreateForAccept(), EncryptTenantSwitchToken(userId: 1, tenantId: 2)));
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_DespatchUserNotFound_IsRejected()
    {
        var h = CreateForAccept();
        h.MasterCtx.Users.Add(new User
        {
            UserId = 50, Email = "ghost@test.com", Password = "x", Salt = "x",
            CurrentTenantId = 1, IsLegacyHash = false, IsCourier = false
        });
        h.MasterCtx.TenantUsers.Add(new TenantUser { TenantUserId = 50, TenantId = 1, UserId = 50 });
        await h.MasterCtx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(await AcceptAsync(h, EncryptTenantSwitchToken(userId: 50, tenantId: 1)));
    }

    [Fact]
    public async Task AcceptTenantSwitchToken_ValidToken_SignsInAndIsAccepted()
    {
        var h = CreateForAccept();

        var accepted = await AcceptAsync(h, EncryptTenantSwitchToken(userId: 1, tenantId: 1));

        Assert.True(accepted);
        await AuthService(h.Http).Received(1).SignInAsync(
            Arg.Any<HttpContext>(), "Identity.Application",
            Arg.Is<ClaimsPrincipal>(p => p.HasClaim("CurrentTenantID", "1") && p.HasClaim("IsNetworkPartner", "False")),
            Arg.Any<AuthenticationProperties>());
    }
}
