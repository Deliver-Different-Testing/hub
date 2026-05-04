using Hub.Controllers;
using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Hub.Tests.Controllers;

[Collection("PartnerDirectoryApiKey")]
public class TenantsControllerTests : IDisposable
{
    private readonly IAuthenticationRepository _mockRepository;
    private readonly TenantsController _controller;
    private const string ValidApiKey = "test-api-key-123";

    public TenantsControllerTests()
    {
        _mockRepository = Substitute.For<IAuthenticationRepository>();
        _controller = new TenantsController(_mockRepository);
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", ValidApiKey);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", null);
    }

    [Fact]
    public async Task GetConnectionString_ValidApiKey_Returns200WithConnection()
    {
        const string connectionString = "Server=hub.test;Database=Tenant42;";
        _mockRepository.GetTenantConnectionStringAsync(42).Returns(connectionString);

        var result = await _controller.GetConnectionString(ValidApiKey, 42);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TenantConnectionStringResponse>(okResult.Value);
        Assert.Equal(42, response.TenantId);
        Assert.Equal(connectionString, response.ConnectionString);
    }

    [Fact]
    public async Task GetConnectionString_MissingApiKey_Returns401()
    {
        var result = await _controller.GetConnectionString(null, 42);

        Assert.IsType<UnauthorizedResult>(result);
        await _mockRepository.DidNotReceive().GetTenantConnectionStringAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task GetConnectionString_WrongApiKey_Returns401()
    {
        var result = await _controller.GetConnectionString("wrong-key", 42);

        Assert.IsType<UnauthorizedResult>(result);
        await _mockRepository.DidNotReceive().GetTenantConnectionStringAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task GetConnectionString_EnvVarNotSet_Returns401()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", null);

        var result = await _controller.GetConnectionString(ValidApiKey, 42);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetConnectionString_TenantNotFound_Returns404()
    {
        _mockRepository.GetTenantConnectionStringAsync(999).Returns((string?)null);

        var result = await _controller.GetConnectionString(ValidApiKey, 999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetConnectionString_EmptyConnectionString_Returns404()
    {
        _mockRepository.GetTenantConnectionStringAsync(7).Returns(string.Empty);

        var result = await _controller.GetConnectionString(ValidApiKey, 7);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetConnectionString_RepositoryThrows_Returns500()
    {
        _mockRepository.GetTenantConnectionStringAsync(1).ThrowsAsync(new Exception("db blew up"));

        var result = await _controller.GetConnectionString(ValidApiKey, 1);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }
}
