using Hub.Controllers;
using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Hub.Tests.Controllers;

[Collection("PartnerDirectoryApiKey")]
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
        _mockService.GetActiveListingsAsync(null)
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
    public async Task GetActiveListings_WithTenantId_PassesTenantIdToService()
    {
        _mockService.GetActiveListingsAsync(42)
            .Returns(new List<PartnerDirectoryListingResponse>());

        var result = await _controller.GetActiveListings(ValidApiKey, 42);

        var okResult = Assert.IsType<OkObjectResult>(result);
        await _mockService.Received(1).GetActiveListingsAsync(42);
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

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(response, okResult.Value);
    }

    [Fact]
    public async Task AddListing_MissingApiKey_Returns401()
    {
        var request = new PartnerDirectoryListingRequest { TenantId = 1, BaseUrl = "https://example.com" };

        var result = await _controller.AddListing(null, request);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AddListing_Duplicate_ReturnsOkWithNull()
    {
        var request = new PartnerDirectoryListingRequest { TenantId = 1, BaseUrl = "https://example.com" };
        _mockService.AddListingAsync(request).Returns((PartnerDirectoryListingResponse?)null);

        var result = await _controller.AddListing(ValidApiKey, request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Null(okResult.Value);
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

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(response, okResult.Value);
    }

    [Fact]
    public async Task UpdateListing_MissingApiKey_Returns401()
    {
        var request = new PartnerDirectoryListingRequest { TenantId = 1, BaseUrl = "https://example.com" };

        var result = await _controller.UpdateListing(null, 1, request);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task UpdateListing_NotFound_ReturnsOkWithNull()
    {
        var request = new PartnerDirectoryListingRequest { TenantId = 99, BaseUrl = "https://example.com" };
        _mockService.UpdateListingAsync(99, request).Returns((PartnerDirectoryListingResponse?)null);

        var result = await _controller.UpdateListing(ValidApiKey, 99, request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Null(okResult.Value);
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

    [Fact]
    public async Task ActivateListing_ValidApiKey_Returns200()
    {
        _mockService.ActivateListingAsync(1).Returns(true);

        var result = await _controller.ActivateListing(ValidApiKey, 1);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ActivateListing_MissingApiKey_Returns401()
    {
        var result = await _controller.ActivateListing(null, 1);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task ActivateListing_NotFound_Returns500()
    {
        _mockService.ActivateListingAsync(99).Returns(false);

        var result = await _controller.ActivateListing(ValidApiKey, 99);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task ActivateListing_ServiceThrows_Returns500()
    {
        _mockService.ActivateListingAsync(1).ThrowsAsync(new Exception("test"));

        var result = await _controller.ActivateListing(ValidApiKey, 1);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetListing_ValidApiKey_Returns200()
    {
        var response = new PartnerDirectoryListingResponse
        {
            TenantId = 1, TenantName = "Test", BaseUrl = "https://example.com"
        };
        _mockService.GetListingAsync(1).Returns(response);

        var result = await _controller.GetListing(ValidApiKey, 1);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(response, okResult.Value);
    }

    [Fact]
    public async Task GetListing_MissingApiKey_Returns401()
    {
        var result = await _controller.GetListing(null, 1);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetActiveListings_ServiceThrows_Returns500()
    {
        _mockService.GetActiveListingsAsync(null).ThrowsAsync(new Exception("test"));

        var result = await _controller.GetActiveListings(ValidApiKey);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetListing_ServiceThrows_Returns500()
    {
        _mockService.GetListingAsync(1).ThrowsAsync(new Exception("test"));

        var result = await _controller.GetListing(ValidApiKey, 1);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task AddListing_ServiceThrows_Returns500()
    {
        _mockService.AddListingAsync(Arg.Any<PartnerDirectoryListingRequest>()).ThrowsAsync(new Exception("test"));
        var request = new PartnerDirectoryListingRequest { TenantId = 1, BaseUrl = "https://example.com" };

        var result = await _controller.AddListing(ValidApiKey, request);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task UpdateListing_ServiceThrows_Returns500()
    {
        _mockService.UpdateListingAsync(1, Arg.Any<PartnerDirectoryListingRequest>()).ThrowsAsync(new Exception("test"));
        var request = new PartnerDirectoryListingRequest { TenantId = 1, BaseUrl = "https://example.com" };

        var result = await _controller.UpdateListing(ValidApiKey, 1, request);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task RemoveListing_ServiceThrows_Returns500()
    {
        _mockService.RemoveListingAsync(1).ThrowsAsync(new Exception("test"));

        var result = await _controller.RemoveListing(ValidApiKey, 1);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task CreateLinkRequest_ValidApiKey_Returns200()
    {
        var request = new LinkRequestCreateRequest
        {
            RequestingTenantId = 1, TargetTenantId = 2, Message = "Hello"
        };
        var response = new LinkRequestResponse
        {
            Id = 1, RequestingTenantId = 1, RequestingTenantName = "Tenant A",
            TargetTenantId = 2, TargetTenantName = "Tenant B", Status = "Pending", Message = "Hello"
        };
        _mockService.CreateLinkRequestAsync(request).Returns(response);

        var result = await _controller.CreateLinkRequest(ValidApiKey, request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(response, okResult.Value);
    }

    [Fact]
    public async Task CreateLinkRequest_MissingApiKey_Returns401()
    {
        var request = new LinkRequestCreateRequest { RequestingTenantId = 1, TargetTenantId = 2 };

        var result = await _controller.CreateLinkRequest(null, request);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task CreateLinkRequest_TenantNotFound_Returns404()
    {
        var request = new LinkRequestCreateRequest { RequestingTenantId = 999, TargetTenantId = 2 };
        _mockService.CreateLinkRequestAsync(request).Returns((LinkRequestResponse?)null);

        var result = await _controller.CreateLinkRequest(ValidApiKey, request);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task CreateLinkRequest_DuplicatePending_Returns409()
    {
        var request = new LinkRequestCreateRequest { RequestingTenantId = 1, TargetTenantId = 2 };
        _mockService.CreateLinkRequestAsync(request)
            .ThrowsAsync(new InvalidOperationException("duplicate"));

        var result = await _controller.CreateLinkRequest(ValidApiKey, request);

        Assert.IsType<ConflictResult>(result);
    }

    [Fact]
    public async Task CreateLinkRequest_ServiceThrows_Returns500()
    {
        var request = new LinkRequestCreateRequest { RequestingTenantId = 1, TargetTenantId = 2 };
        _mockService.CreateLinkRequestAsync(request).ThrowsAsync(new Exception("test"));

        var result = await _controller.CreateLinkRequest(ValidApiKey, request);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetLinkRequests_ValidApiKey_Returns200()
    {
        var responses = new List<LinkRequestResponse>
        {
            new()
            {
                Id = 1, RequestingTenantId = 1, RequestingTenantName = "A",
                TargetTenantId = 2, TargetTenantName = "B", Status = "Pending"
            }
        };
        _mockService.GetLinkRequestsAsync(1).Returns(responses);

        var result = await _controller.GetLinkRequests(ValidApiKey, 1);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<IReadOnlyList<LinkRequestResponse>>(okResult.Value, exactMatch: false);
        Assert.Single(list);
    }

    [Fact]
    public async Task GetLinkRequests_MissingApiKey_Returns401()
    {
        var result = await _controller.GetLinkRequests(null, 1);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetLinkRequests_ServiceThrows_Returns500()
    {
        _mockService.GetLinkRequestsAsync(1).ThrowsAsync(new Exception("test"));

        var result = await _controller.GetLinkRequests(ValidApiKey, 1);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task AcceptLinkRequest_ValidApiKey_Returns200()
    {
        var response = new LinkRequestResponse
        {
            Id = 1, RequestingTenantId = 1, RequestingTenantName = "A",
            TargetTenantId = 2, TargetTenantName = "B", Status = "Accepted"
        };
        _mockService.AcceptLinkRequestAsync(1).Returns(response);

        var result = await _controller.AcceptLinkRequest(ValidApiKey, 1);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(response, okResult.Value);
    }

    [Fact]
    public async Task AcceptLinkRequest_MissingApiKey_Returns401()
    {
        var result = await _controller.AcceptLinkRequest(null, 1);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AcceptLinkRequest_NotFound_Returns404()
    {
        _mockService.AcceptLinkRequestAsync(99).Returns((LinkRequestResponse?)null);

        var result = await _controller.AcceptLinkRequest(ValidApiKey, 99);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task AcceptLinkRequest_ServiceThrows_Returns500()
    {
        _mockService.AcceptLinkRequestAsync(1).ThrowsAsync(new Exception("test"));

        var result = await _controller.AcceptLinkRequest(ValidApiKey, 1);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task DeclineLinkRequest_ValidApiKey_Returns200()
    {
        var response = new LinkRequestResponse
        {
            Id = 1, RequestingTenantId = 1, RequestingTenantName = "A",
            TargetTenantId = 2, TargetTenantName = "B", Status = "Declined", DeclineReason = "No thanks"
        };
        _mockService.DeclineLinkRequestAsync(1, "No thanks").Returns(response);

        var result = await _controller.DeclineLinkRequest(ValidApiKey, 1,
            new DeclineLinkRequestRequest { Reason = "No thanks" });

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(response, okResult.Value);
    }

    [Fact]
    public async Task DeclineLinkRequest_MissingApiKey_Returns401()
    {
        var result = await _controller.DeclineLinkRequest(null, 1, null);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task DeclineLinkRequest_NotFound_Returns404()
    {
        _mockService.DeclineLinkRequestAsync(99, null).Returns((LinkRequestResponse?)null);

        var result = await _controller.DeclineLinkRequest(ValidApiKey, 99, null);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeclineLinkRequest_ServiceThrows_Returns500()
    {
        _mockService.DeclineLinkRequestAsync(1, Arg.Any<string?>()).ThrowsAsync(new Exception("test"));

        var result = await _controller.DeclineLinkRequest(ValidApiKey, 1, null);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task ClearLinkRequest_ValidApiKey_Returns200()
    {
        var response = new LinkRequestResponse
        {
            Id = 1, RequestingTenantId = 1, RequestingTenantName = "A",
            TargetTenantId = 2, TargetTenantName = "B", Status = "Declined", DeclineReason = "Stale"
        };
        _mockService.ClearLinkRequestAsync(1, "Stale").Returns(response);

        var result = await _controller.ClearLinkRequest(ValidApiKey, 1,
            new DeclineLinkRequestRequest { Reason = "Stale" });

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(response, okResult.Value);
    }

    [Fact]
    public async Task ClearLinkRequest_MissingApiKey_Returns401()
    {
        var result = await _controller.ClearLinkRequest(null, 1, null);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task ClearLinkRequest_NotFound_Returns404()
    {
        _mockService.ClearLinkRequestAsync(99, null).Returns((LinkRequestResponse?)null);

        var result = await _controller.ClearLinkRequest(ValidApiKey, 99, null);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ClearLinkRequest_ServiceThrows_Returns500()
    {
        _mockService.ClearLinkRequestAsync(1, Arg.Any<string?>()).ThrowsAsync(new Exception("test"));

        var result = await _controller.ClearLinkRequest(ValidApiKey, 1, null);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }
}
