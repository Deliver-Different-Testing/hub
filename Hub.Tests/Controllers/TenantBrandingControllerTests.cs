using FluentAssertions;
using Hub.Controllers;
using Hub.Services;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Hub.Tests.Controllers;

public class TenantBrandingControllerTests
{
    private readonly Mock<ITenantBrandingConfigService> _mockService;
    private readonly TenantBrandingController _controller;

    public TenantBrandingControllerTests()
    {
        _mockService = new Mock<ITenantBrandingConfigService>();
        _controller = new TenantBrandingController(_mockService.Object);
    }

    [Fact]
    public async Task GetReportConfig_ExistingTenant_Returns200()
    {
        _mockService
            .Setup(s => s.GetReportConfigAsync(1))
            .ReturnsAsync(new TenantBrandingResponse
            {
                TenantId = 1,
                CompanyName = "Test Company"
            });

        var result = await _controller.GetReportConfig(1);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<TenantBrandingResponse>().Subject;
        response.CompanyName.Should().Be("Test Company");
    }

    [Fact]
    public async Task GetReportConfig_NonExistentTenant_Returns404()
    {
        _mockService
            .Setup(s => s.GetReportConfigAsync(999))
            .ReturnsAsync((TenantBrandingResponse?)null);

        var result = await _controller.GetReportConfig(999);

        result.Should().BeOfType<NotFoundResult>();
    }
}
