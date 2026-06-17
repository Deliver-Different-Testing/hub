using Hub.Controllers;
using Hub.Interfaces;
using Hub.Models;
using Hub.Models.Master;
using Hub.Repositories;
using Hub.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Hub.Tests.Controllers;

[Collection("EnvironmentVariables")]
public class AdminUsersControllerTests : IDisposable
{
    private const string ValidApiKey = "test-configurator-key";

    private readonly string _originalApiKey;
    private readonly string _originalCredentials;
    private readonly string _originalReply;
    private readonly string _originalResetLink;

    private readonly IConnectionStringManager _connectionStringManager;
    private readonly IAuthenticationRepository _authRepo;
    private readonly IDespatchRepository _despatchRepo;
    private readonly AdminUsersController _controller;

    public AdminUsersControllerTests()
    {
        _originalApiKey = Environment.GetEnvironmentVariable("ConfiguratorApiKey") ?? string.Empty;
        _originalCredentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        _originalReply = Environment.GetEnvironmentVariable("ReplyEmail") ?? string.Empty;
        _originalResetLink = Environment.GetEnvironmentVariable("ResetBaseLink") ?? string.Empty;

        Environment.SetEnvironmentVariable("ConfiguratorApiKey", ValidApiKey);
        Environment.SetEnvironmentVariable("SQLCredentials", ";User=test;Password=test;");
        Environment.SetEnvironmentVariable("ReplyEmail", "noreply@test.com");
        Environment.SetEnvironmentVariable("ResetBaseLink", "https://test.com/reset");

        _connectionStringManager = Substitute.For<IConnectionStringManager>();
        _authRepo = Substitute.For<IAuthenticationRepository>();
        _despatchRepo = Substitute.For<IDespatchRepository>();
        _controller = new AdminUsersController(_connectionStringManager, _authRepo, _despatchRepo);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ConfiguratorApiKey", _originalApiKey);
        Environment.SetEnvironmentVariable("SQLCredentials", _originalCredentials);
        Environment.SetEnvironmentVariable("ReplyEmail", _originalReply);
        Environment.SetEnvironmentVariable("ResetBaseLink", _originalResetLink);
        GC.SuppressFinalize(this);
    }

    private static (AdminUsersController controller, MasterContext masterCtx) CreateControllerWithDb()
    {
        var masterCtx = TestMasterContextFactory.CreateWithSeedData();
        var despatchCtx = TestDespatchContextFactory.CreateWithSeedData();

        var mockProcs = Substitute.For<IDespatchContextProcedures>();
        mockProcs.NET_stpContact_ResetPasswordAsync(
                Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<OutputParameter<int>>(), Arg.Any<CancellationToken>())
            .Returns(1);
        despatchCtx.Procedures = mockProcs;

        var connectionStringManager = new ConnectionStringManager();
        var authRepo = new AuthenticationRepository(masterCtx);
        var tenantService = Substitute.For<ITenantService>();
        var despatchRepo = new Repository(despatchCtx, tenantService);

        var controller = new AdminUsersController(connectionStringManager, authRepo, despatchRepo);
        ControllerTestBase.SetupHttpContext(controller);
        return (controller, masterCtx);
    }

    private static AdminUsersController.CreateNpUserRequest ValidRequest(
        string email = "new.user@example.com",
        int tenantId = 42)
        => new(email, tenantId);

    private static User UserStub(int userId = 100, string email = "new.user@example.com", string resetKey = "RESET-KEY-ABC")
        => new()
        {
            UserId = userId,
            Email = email,
            ResetKey = resetKey,
            Password = string.Empty,
            Salt = string.Empty
        };

    private static TucClientContact ContactStub(int ucctId = 555)
        => new()
        {
            UcctId = ucctId,
            UcctFirstname = "First",
            UcctSurname = "Last",
            UcctEmail = "new.user@example.com"
        };

    // --- API-key gate (mock-based, exercise Create NP endpoint) -----------

    [Fact]
    public async Task Create_MissingApiKey_Returns401()
    {
        var result = await _controller.Create(null, ValidRequest());

        Assert.IsType<UnauthorizedResult>(result);
        await _authRepo.DidNotReceive().CreateUserAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Create_WrongApiKey_Returns401()
    {
        var result = await _controller.Create("wrong-key", ValidRequest());

        Assert.IsType<UnauthorizedResult>(result);
        await _authRepo.DidNotReceive().CreateUserAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Create_EnvVarNotSet_Returns401()
    {
        Environment.SetEnvironmentVariable("ConfiguratorApiKey", null);

        var result = await _controller.Create(ValidApiKey, ValidRequest());

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Create_EnvVarEmpty_Returns401()
    {
        // Defence-in-depth: even if both the header and env var are empty
        // strings, the request must NOT authenticate.
        Environment.SetEnvironmentVariable("ConfiguratorApiKey", "");

        var result = await _controller.Create("", ValidRequest());

        Assert.IsType<UnauthorizedResult>(result);
    }

    // --- Request validation ---------------------------------------------

    [Fact]
    public async Task Create_NullRequest_ReturnsBadRequest()
    {
        var result = await _controller.Create(ValidApiKey, null!);

        Assert.IsType<BadRequestObjectResult>(result);
        await _authRepo.DidNotReceive().CreateUserAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Create_BlankEmail_ReturnsBadRequest(string? email)
    {
        var result = await _controller.Create(ValidApiKey, new AdminUsersController.CreateNpUserRequest(email!, 1));

        Assert.IsType<BadRequestObjectResult>(result);
        await _authRepo.DidNotReceive().CreateUserAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Create_NonPositiveTenantId_ReturnsBadRequest(int tenantId)
    {
        var result = await _controller.Create(ValidApiKey, ValidRequest(tenantId: tenantId));

        Assert.IsType<BadRequestObjectResult>(result);
        await _authRepo.DidNotReceive().CreateUserAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>());
    }

    // --- Master.User outcomes -------------------------------------------

    [Fact]
    public async Task Create_DuplicateEmail_Returns409()
    {
        _authRepo.CreateUserAsync("new.user@example.com", 42, true).Returns((User?)null);

        var result = await _controller.Create(ValidApiKey, ValidRequest());

        Assert.IsType<ConflictObjectResult>(result);
        await _authRepo.DidNotReceive().GetTenantConnectionStringAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Create_TenantHasNoConnectionString_ReturnsOkWithInviteFalse()
    {
        _authRepo.CreateUserAsync("new.user@example.com", 42, true).Returns(UserStub());
        _authRepo.GetTenantConnectionStringAsync(42).Returns(string.Empty);

        var result = await _controller.Create(ValidApiKey, ValidRequest());

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminUsersController.CreateNpUserResponse>(ok.Value);
        Assert.Equal(100, body.UserId);
        Assert.Equal("new.user@example.com", body.Email);
        Assert.False(body.InviteEmailSent);
        _connectionStringManager.DidNotReceive().SetConnectionString(Arg.Any<string>());
        await _despatchRepo.DidNotReceive().FetchUserByUsername(Arg.Any<string>());
    }

    [Fact]
    public async Task Create_ContactMissingOnTenant_ReturnsOkWithInviteFalse()
    {
        _authRepo.CreateUserAsync("new.user@example.com", 42, true).Returns(UserStub());
        _authRepo.GetTenantConnectionStringAsync(42).Returns("Server=tenant-db;Database=foo;");
        _despatchRepo.FetchUserByUsername("new.user@example.com").Returns((TucClientContact?)null);

        var result = await _controller.Create(ValidApiKey, ValidRequest());

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminUsersController.CreateNpUserResponse>(ok.Value);
        Assert.False(body.InviteEmailSent);
        _connectionStringManager.Received(1)
            .SetConnectionString("Server=tenant-db;Database=foo;;User=test;Password=test;");
        await _despatchRepo.DidNotReceive()
            .InitiatePasswordReset(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Create_InviteEmailThrows_ReturnsOkWithInviteFalse()
    {
        _authRepo.CreateUserAsync("new.user@example.com", 42, true).Returns(UserStub());
        _authRepo.GetTenantConnectionStringAsync(42).Returns("Server=tenant-db;Database=foo;");
        _despatchRepo.FetchUserByUsername("new.user@example.com").Returns(ContactStub());
        _despatchRepo.InitiatePasswordReset(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await _controller.Create(ValidApiKey, ValidRequest());

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminUsersController.CreateNpUserResponse>(ok.Value);
        Assert.False(body.InviteEmailSent);
    }

    [Fact]
    public async Task Create_HappyPath_Returns201AndDispatchesInvite()
    {
        _authRepo.CreateUserAsync("new.user@example.com", 42, true)
            .Returns(UserStub(userId: 100, resetKey: "RESET-KEY-ABC"));
        _authRepo.GetTenantConnectionStringAsync(42).Returns("Server=tenant-db;Database=foo;");
        _despatchRepo.FetchUserByUsername("new.user@example.com").Returns(ContactStub(ucctId: 555));

        var result = await _controller.Create(ValidApiKey, ValidRequest());

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(AdminUsersController.Create), created.ActionName);
        var body = Assert.IsType<AdminUsersController.CreateNpUserResponse>(created.Value);
        Assert.Equal(100, body.UserId);
        Assert.Equal("new.user@example.com", body.Email);
        Assert.True(body.InviteEmailSent);

        await _despatchRepo.Received(1).InitiatePasswordReset(
            555,
            "new.user@example.com",
            "noreply@test.com",
            "https://test.com/reset?code=RESET-KEY-ABC");
    }

    [Fact]
    public async Task Create_HappyPath_WithMissingEmailEnvVars_UsesEmptyStrings()
    {
        // ReplyEmail / ResetBaseLink absent — link is still dispatched but
        // built from empty strings (the configurator's deployment contract
        // is to set these, so absence is non-fatal at the call boundary).
        Environment.SetEnvironmentVariable("ReplyEmail", null);
        Environment.SetEnvironmentVariable("ResetBaseLink", null);

        _authRepo.CreateUserAsync("new.user@example.com", 42, true)
            .Returns(UserStub(resetKey: "RK"));
        _authRepo.GetTenantConnectionStringAsync(42).Returns("Server=tenant-db;Database=foo;");
        _despatchRepo.FetchUserByUsername("new.user@example.com").Returns(ContactStub(ucctId: 555));

        var result = await _controller.Create(ValidApiKey, ValidRequest());

        Assert.IsType<CreatedAtActionResult>(result);
        await _despatchRepo.Received(1)
            .InitiatePasswordReset(555, "new.user@example.com", string.Empty, "?code=RK");
    }

    // --- Outer try/catch: unexpected failures ---------------------------

    [Fact]
    public async Task Create_AuthRepoThrows_Returns500()
    {
        _authRepo.CreateUserAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>())
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _controller.Create(ValidApiKey, ValidRequest());

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, status.StatusCode);
    }

    [Fact]
    public async Task Create_TenantLookupThrows_Returns500()
    {
        _authRepo.CreateUserAsync("new.user@example.com", 42, true).Returns(UserStub());
        _authRepo.GetTenantConnectionStringAsync(42)
            .ThrowsAsync(new InvalidOperationException("lookup broke"));

        var result = await _controller.Create(ValidApiKey, ValidRequest());

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, status.StatusCode);
    }

    [Fact]
    public async Task Create_SqlCredentialsMissing_Returns500()
    {
        // SetTenantConnectionString throws InvalidOperationException when
        // the SQLCredentials env var is unset; the outer catch turns it
        // into a 500 (no rollback of the Master.User row by design).
        Environment.SetEnvironmentVariable("SQLCredentials", null);

        _authRepo.CreateUserAsync("new.user@example.com", 42, true).Returns(UserStub());
        _authRepo.GetTenantConnectionStringAsync(42).Returns("Server=tenant-db;Database=foo;");

        var result = await _controller.Create(ValidApiKey, ValidRequest());

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, status.StatusCode);
        _connectionStringManager.DidNotReceive().SetConnectionString(Arg.Any<string>());
    }

    // --- CreateTenantUser endpoint (DB-context based) -------------------

    [Fact]
    public async Task CreateTenantUser_InvalidApiKey_ReturnsUnauthorized()
    {
        var (controller, _) = CreateControllerWithDb();

        var result = await controller.CreateTenantUser("wrong-key",
            new AdminUsersController.CreateNpUserRequest("john@test.com", 1));

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task CreateTenantUser_MissingEmail_ReturnsBadRequest()
    {
        var (controller, _) = CreateControllerWithDb();

        var result = await controller.CreateTenantUser(ValidApiKey,
            new AdminUsersController.CreateNpUserRequest("", 1));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateTenantUser_NewUserWithContact_CreatesTenantMasterRowAndSendsInvite()
    {
        var (controller, masterCtx) = CreateControllerWithDb();

        // john@test.com has an active tucClientContact (UcctId 1) but no Master.User yet.
        var result = await controller.CreateTenantUser(ValidApiKey,
            new AdminUsersController.CreateNpUserRequest("john@test.com", 1));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var body = Assert.IsType<AdminUsersController.CreateNpUserResponse>(created.Value);
        Assert.True(body.InviteEmailSent);

        // The Master row must exist, be a tenant user (NOT a network partner),
        // not a courier, and carry a ResetKey for the invite link.
        var user = masterCtx.Users.Single(u => u.Email == "john@test.com");
        Assert.False(user.IsNetworkPartner!.Value);
        Assert.False(user.IsCourier ?? false);
        Assert.False(string.IsNullOrEmpty(user.ResetKey));
    }

    [Fact]
    public async Task CreateTenantUser_EmailAlreadyExists_ReturnsConflict()
    {
        var (controller, _) = CreateControllerWithDb();

        // staff@test.com already exists in Master.User (seed UserId 1).
        var result = await controller.CreateTenantUser(ValidApiKey,
            new AdminUsersController.CreateNpUserRequest("staff@test.com", 1));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task CreateTenantUser_NoDespatchContact_CreatesRowButInviteNotSent()
    {
        var (controller, masterCtx) = CreateControllerWithDb();

        // ghost@test.com has neither a Master.User nor a tucClientContact.
        var result = await controller.CreateTenantUser(ValidApiKey,
            new AdminUsersController.CreateNpUserRequest("ghost@test.com", 1));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminUsersController.CreateNpUserResponse>(ok.Value);
        Assert.False(body.InviteEmailSent);

        // Row is still created as a tenant user (partial-success contract).
        var user = masterCtx.Users.Single(u => u.Email == "ghost@test.com");
        Assert.False(user.IsNetworkPartner!.Value);
    }

    [Fact]
    public async Task Create_NpEndpoint_StillStampsIsNetworkPartnerTrue()
    {
        var (controller, masterCtx) = CreateControllerWithDb();

        var result = await controller.Create(ValidApiKey,
            new AdminUsersController.CreateNpUserRequest("john@test.com", 1));

        Assert.IsType<CreatedAtActionResult>(result);
        var user = masterCtx.Users.Single(u => u.Email == "john@test.com");
        Assert.True(user.IsNetworkPartner!.Value);
    }

    // === Item 7b — SetPassword (staff password set) =====================

    private static User StaffUserStub(int userId = 100, string email = "staff.user@example.com")
        => new()
        {
            UserId = userId,
            Email = email,
            ResetKey = "OLD-RESET-KEY",
            Password = "OLD-HASH",
            Salt = "OLD-SALT",
            IsLegacyHash = true,
        };

    private static User StaffUserWithTenant(string dbConnection = "Server=tenant-db;Database=foo;")
    {
        var u = StaffUserStub();
        u.CurrentTenant = new Tenant { Dbconnection = dbConnection };
        return u;
    }

    [Fact]
    public async Task SetPassword_MissingApiKey_Returns401()
    {
        var result = await _controller.SetPassword(null,
            new AdminUsersController.SetPasswordRequest("staff.user@example.com", "Password1!"));

        Assert.IsType<UnauthorizedResult>(result);
        await _authRepo.DidNotReceive().GetUserByEmail(Arg.Any<string>(), Arg.Any<bool?>());
    }

    [Theory]
    [InlineData("", "Password1!")]
    [InlineData("   ", "Password1!")]
    [InlineData("staff.user@example.com", "")]
    [InlineData("staff.user@example.com", "short")]   // < 8 chars
    public async Task SetPassword_InvalidInput_ReturnsBadRequest(string email, string password)
    {
        var result = await _controller.SetPassword(ValidApiKey,
            new AdminUsersController.SetPasswordRequest(email, password));

        Assert.IsType<BadRequestObjectResult>(result);
        await _authRepo.DidNotReceive().SaveAsync();
    }

    [Fact]
    public async Task SetPassword_UserNotFound_Returns404()
    {
        _authRepo.GetUserByEmail("staff.user@example.com", false).Returns((User?)null);

        var result = await _controller.SetPassword(ValidApiKey,
            new AdminUsersController.SetPasswordRequest("staff.user@example.com", "Password1!"));

        Assert.IsType<NotFoundObjectResult>(result);
        await _authRepo.DidNotReceive().SaveAsync();
    }

    [Fact]
    public async Task SetPassword_HappyPath_HashesAndClearsResetKey()
    {
        var user = StaffUserStub();
        _authRepo.GetUserByEmail("staff.user@example.com", false).Returns(user);

        var result = await _controller.SetPassword(ValidApiKey,
            new AdminUsersController.SetPasswordRequest("staff.user@example.com", "Password1!"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminUsersController.SetPasswordResponse>(ok.Value);
        Assert.Equal(100, body.UserId);

        Assert.NotEqual("OLD-HASH", user.Password);
        Assert.NotEqual("OLD-SALT", user.Salt);
        Assert.False(string.IsNullOrEmpty(user.Password));
        Assert.False(user.IsLegacyHash);          // re-hashed with the modern scheme
        Assert.Null(user.ResetKey);               // any outstanding reset link voided
        await _authRepo.Received(1).SaveAsync();
    }

    [Fact]
    public async Task SetPassword_LooksUpStaffNotCourier()
    {
        var user = StaffUserStub();
        _authRepo.GetUserByEmail("staff.user@example.com", false).Returns(user);

        await _controller.SetPassword(ValidApiKey,
            new AdminUsersController.SetPasswordRequest("staff.user@example.com", "Password1!"));

        // isCourier:false scopes the lookup to staff — couriers use a separate scheme.
        await _authRepo.Received(1).GetUserByEmail("staff.user@example.com", false);
    }

    [Fact]
    public async Task SetPassword_AuthRepoThrows_Returns500()
    {
        _authRepo.GetUserByEmail(Arg.Any<string>(), Arg.Any<bool?>())
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _controller.SetPassword(ValidApiKey,
            new AdminUsersController.SetPasswordRequest("staff.user@example.com", "Password1!"));

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, status.StatusCode);
    }

    // === Item 7b — SendReset (staff reset email) ========================

    [Fact]
    public async Task SendReset_MissingApiKey_Returns401()
    {
        var result = await _controller.SendReset(null,
            new AdminUsersController.SendResetRequest("staff.user@example.com"));

        Assert.IsType<UnauthorizedResult>(result);
        await _authRepo.DidNotReceive().GetUserByEmail(Arg.Any<string>(), Arg.Any<bool?>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendReset_BlankEmail_ReturnsBadRequest(string email)
    {
        var result = await _controller.SendReset(ValidApiKey,
            new AdminUsersController.SendResetRequest(email));

        Assert.IsType<BadRequestObjectResult>(result);
        await _authRepo.DidNotReceive().SaveAsync();
    }

    [Fact]
    public async Task SendReset_UserNotFound_Returns404()
    {
        _authRepo.GetUserByEmail("staff.user@example.com", false).Returns((User?)null);

        var result = await _controller.SendReset(ValidApiKey,
            new AdminUsersController.SendResetRequest("staff.user@example.com"));

        Assert.IsType<NotFoundObjectResult>(result);
        await _authRepo.DidNotReceive().SaveAsync();
    }

    [Fact]
    public async Task SendReset_NoTenantConnection_ReturnsOkWithResetFalse()
    {
        var user = StaffUserStub();   // no CurrentTenant
        _authRepo.GetUserByEmail("staff.user@example.com", false).Returns(user);

        var result = await _controller.SendReset(ValidApiKey,
            new AdminUsersController.SendResetRequest("staff.user@example.com"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminUsersController.SendResetResponse>(ok.Value);
        Assert.False(body.ResetEmailSent);
        // A fresh reset key was still generated + persisted.
        Assert.NotEqual("OLD-RESET-KEY", user.ResetKey);
        await _authRepo.Received(1).SaveAsync();
        _connectionStringManager.DidNotReceive().SetConnectionString(Arg.Any<string>());
    }

    [Fact]
    public async Task SendReset_ContactMissingOnTenant_ReturnsOkWithResetFalse()
    {
        _authRepo.GetUserByEmail("staff.user@example.com", false).Returns(StaffUserWithTenant());
        _despatchRepo.FetchUserByUsername("staff.user@example.com").Returns((TucClientContact?)null);

        var result = await _controller.SendReset(ValidApiKey,
            new AdminUsersController.SendResetRequest("staff.user@example.com"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminUsersController.SendResetResponse>(ok.Value);
        Assert.False(body.ResetEmailSent);
        _connectionStringManager.Received(1)
            .SetConnectionString("Server=tenant-db;Database=foo;;User=test;Password=test;");
        await _despatchRepo.DidNotReceive()
            .InitiatePasswordReset(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task SendReset_EmailThrows_ReturnsOkWithResetFalse()
    {
        _authRepo.GetUserByEmail("staff.user@example.com", false).Returns(StaffUserWithTenant());
        _despatchRepo.FetchUserByUsername("staff.user@example.com").Returns(ContactStub());
        _despatchRepo.InitiatePasswordReset(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await _controller.SendReset(ValidApiKey,
            new AdminUsersController.SendResetRequest("staff.user@example.com"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminUsersController.SendResetResponse>(ok.Value);
        Assert.False(body.ResetEmailSent);
    }

    [Fact]
    public async Task SendReset_HappyPath_DispatchesResetEmailWithFreshKey()
    {
        var user = StaffUserWithTenant();
        _authRepo.GetUserByEmail("staff.user@example.com", false).Returns(user);
        _despatchRepo.FetchUserByUsername("staff.user@example.com").Returns(ContactStub(ucctId: 555));

        var result = await _controller.SendReset(ValidApiKey,
            new AdminUsersController.SendResetRequest("staff.user@example.com"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminUsersController.SendResetResponse>(ok.Value);
        Assert.True(body.ResetEmailSent);

        await _despatchRepo.Received(1).InitiatePasswordReset(
            555,
            "staff.user@example.com",
            "noreply@test.com",
            $"https://test.com/reset?code={user.ResetKey}");
    }

    [Fact]
    public async Task SendReset_AuthRepoThrows_Returns500()
    {
        _authRepo.GetUserByEmail(Arg.Any<string>(), Arg.Any<bool?>())
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _controller.SendReset(ValidApiKey,
            new AdminUsersController.SendResetRequest("staff.user@example.com"));

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, status.StatusCode);
    }
}
