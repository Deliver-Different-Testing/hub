using Hub.Controllers;
using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Controllers;

public class PartnerDirectoryControllerTests : IDisposable
{
    private readonly IPartnerDirectoryService _mockService;
    private readonly PartnerDirectoryController _controller;
    private const string ValidApiKey = "test-api-key-123";

    public PartnerDirectoryControllerTests()
    {
        _mockService = Substitute.For<IPartnerDirectoryService>();
        _controller = new PartnerDirectoryController(_mockService);
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", ValidApiKey);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", null);
    }

    [Fact]
    public async Task GetActiveListings_ValidApiKey_Returns200()
    {
        _mockService.GetActiveListingsAsync()
            .Returns(new List<PartnerDirectoryListingResponse>
            {
                new()
                {
                    TenantId = 1,
                    TenantName = "Test",
                    BaseUrl = "https://example.com",
                    Description = "Desc",
                    Region = "NZ"
                }
            });

        var result = await _controller.GetActiveListings(ValidApiKey);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var listings = Assert.IsType<IReadOnlyList<PartnerDirectoryListingResponse>>(okResult.Value, exactMatch: false);
        Assert.Single(listings);
    }

    [Fact]
    public async Task GetActiveListings_MissingApiKey_Returns401()
    {
        var result = await _controller.GetActiveListings(null);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetActiveListings_WrongApiKey_Returns401()
    {
        var result = await _controller.GetActiveListings("wrong-key");

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetActiveListings_EnvVarNotSet_Returns401()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", null);

        var result = await _controller.GetActiveListings(ValidApiKey);

        Assert.IsType<UnauthorizedResult>(result);
    }
}
