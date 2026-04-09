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

        var jsonResult = Assert.IsType<JsonResult>(result);
        var listings = Assert.IsType<IReadOnlyList<PartnerDirectoryListingResponse>>(jsonResult.Value, exactMatch: false);
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

    [Fact]
    public async Task AddListing_ValidApiKey_Returns200()
    {
        var request = new PartnerDirectoryListingRequest
        {
            TenantId = 1,
            BaseUrl = "https://example.com",
            Description = "Desc",
            Region = "NZ"
        };
        var response = new PartnerDirectoryListingResponse
        {
            TenantId = 1,
            TenantName = "Test",
            BaseUrl = "https://example.com",
            Description = "Desc",
            Region = "NZ"
        };
        _mockService.AddListingAsync(request).Returns(response);

        var result = await _controller.AddListing(ValidApiKey, request);

        var jsonResult = Assert.IsType<JsonResult>(result);
        Assert.Equal(response, jsonResult.Value);
    }

    [Fact]
    public async Task AddListing_MissingApiKey_Returns401()
    {
        var request = new PartnerDirectoryListingRequest { TenantId = 1, BaseUrl = "https://example.com" };

        var result = await _controller.AddListing(null, request);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AddListing_Duplicate_ReturnsJsonWithNull()
    {
        var request = new PartnerDirectoryListingRequest { TenantId = 1, BaseUrl = "https://example.com" };
        _mockService.AddListingAsync(request).Returns((PartnerDirectoryListingResponse?)null);

        var result = await _controller.AddListing(ValidApiKey, request);

        var jsonResult = Assert.IsType<JsonResult>(result);
        Assert.Null(jsonResult.Value);
    }

    [Fact]
    public async Task UpdateListing_ValidApiKey_Returns200()
    {
        var request = new PartnerDirectoryListingRequest
        {
            TenantId = 1,
            BaseUrl = "https://updated.com",
            Description = "Updated",
            Region = "AU"
        };
        var response = new PartnerDirectoryListingResponse
        {
            TenantId = 1,
            TenantName = "Test",
            BaseUrl = "https://updated.com",
            Description = "Updated",
            Region = "AU"
        };
        _mockService.UpdateListingAsync(1, request).Returns(response);

        var result = await _controller.UpdateListing(ValidApiKey, 1, request);

        var jsonResult = Assert.IsType<JsonResult>(result);
        Assert.Equal(response, jsonResult.Value);
    }

    [Fact]
    public async Task UpdateListing_MissingApiKey_Returns401()
    {
        var request = new PartnerDirectoryListingRequest { TenantId = 1, BaseUrl = "https://example.com" };

        var result = await _controller.UpdateListing(null, 1, request);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task UpdateListing_NotFound_ReturnsJsonWithNull()
    {
        var request = new PartnerDirectoryListingRequest { TenantId = 99, BaseUrl = "https://example.com" };
        _mockService.UpdateListingAsync(99, request).Returns((PartnerDirectoryListingResponse?)null);

        var result = await _controller.UpdateListing(ValidApiKey, 99, request);

        var jsonResult = Assert.IsType<JsonResult>(result);
        Assert.Null(jsonResult.Value);
    }

    [Fact]
    public async Task RemoveListing_ValidApiKey_Returns200()
    {
        _mockService.RemoveListingAsync(1).Returns(true);

        var result = await _controller.RemoveListing(ValidApiKey, 1);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task RemoveListing_MissingApiKey_Returns401()
    {
        var result = await _controller.RemoveListing(null, 1);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task RemoveListing_NotFound_Returns500()
    {
        _mockService.RemoveListingAsync(99).Returns(false);

        var result = await _controller.RemoveListing(ValidApiKey, 99);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }
}
