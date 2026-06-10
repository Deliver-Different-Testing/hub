using Hub.Controllers;
using Hub.Interfaces;
using Hub.Models;
using Hub.Models.Master;
using Hub.Repositories;
using Hub.Shared;
using Hub.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Controllers;

[Collection("EnvironmentVariables")]
public class AdminUsersControllerTests : IDisposable
{
    private const string ApiKey = "test-config-key";
    private readonly string _originalApiKey;
    private readonly string _originalCredentials;

    public AdminUsersControllerTests()
    {
        _originalApiKey = Environment.GetEnvironmentVariable("ConfiguratorApiKey") ?? string.Empty;
        _originalCredentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        Environment.SetEnvironmentVariable("ConfiguratorApiKey", ApiKey);
        Environment.SetEnvironmentVariable("SQLCredentials", ";User=test;Password=test;");
        Environment.SetEnvironmentVariable("ReplyEmail", "noreply@test.com");
        Environment.SetEnvironmentVariable("ResetBaseLink", "https://test.com/reset");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ConfiguratorApiKey", _originalApiKey);
        Environment.SetEnvironmentVariable("SQLCredentials", _originalCredentials);
        GC.SuppressFinalize(this);
    }

    private static (AdminUsersController controller, MasterContext masterCtx) CreateController()
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

    [Fact]
    public async Task CreateTenantUser_InvalidApiKey_ReturnsUnauthorized()
    {
        var (controller, _) = CreateController();

        var result = await controller.CreateTenantUser("wrong-key",
            new AdminUsersController.CreateNpUserRequest("john@test.com", 1));

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task CreateTenantUser_MissingEmail_ReturnsBadRequest()
    {
        var (controller, _) = CreateController();

        var result = await controller.CreateTenantUser(ApiKey,
            new AdminUsersController.CreateNpUserRequest("", 1));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateTenantUser_NewUserWithContact_CreatesTenantMasterRowAndSendsInvite()
    {
        var (controller, masterCtx) = CreateController();

        // john@test.com has an active tucClientContact (UcctId 1) but no Master.User yet.
        var result = await controller.CreateTenantUser(ApiKey,
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
        var (controller, _) = CreateController();

        // staff@test.com already exists in Master.User (seed UserId 1).
        var result = await controller.CreateTenantUser(ApiKey,
            new AdminUsersController.CreateNpUserRequest("staff@test.com", 1));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task CreateTenantUser_NoDespatchContact_CreatesRowButInviteNotSent()
    {
        var (controller, masterCtx) = CreateController();

        // ghost@test.com has neither a Master.User nor a tucClientContact.
        var result = await controller.CreateTenantUser(ApiKey,
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
        var (controller, masterCtx) = CreateController();

        var result = await controller.Create(ApiKey,
            new AdminUsersController.CreateNpUserRequest("john@test.com", 1));

        Assert.IsType<CreatedAtActionResult>(result);
        var user = masterCtx.Users.Single(u => u.Email == "john@test.com");
        Assert.True(user.IsNetworkPartner!.Value);
    }
}
