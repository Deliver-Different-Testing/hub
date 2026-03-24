using Hub.Controllers;
using Hub.Interfaces;
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
                CompanyName = "Test Company",
                AddressLines = [],
                Country = string.Empty,
                Phone = string.Empty,
                Email = string.Empty,
                Website = string.Empty,
                LogoUrl = string.Empty,
                PrimaryColour = string.Empty,
                HeaderTextColour = string.Empty,
                AccentColour = string.Empty,
                FooterText = string.Empty,
                DisclaimerText = string.Empty,
                PaperSize = string.Empty,
                TimeZoneId = string.Empty,
                CountryCode = string.Empty
            });

        var result = await _controller.GetReportConfig(1);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TenantBrandingResponse>(okResult.Value);
        Assert.Equal("Test Company", response.CompanyName);
    }

    [Fact]
    public async Task GetReportConfig_NonExistentTenant_Returns404()
    {
        _mockService
            .Setup(s => s.GetReportConfigAsync(999))
            .ReturnsAsync((TenantBrandingResponse?)null);

        var result = await _controller.GetReportConfig(999);

        Assert.IsType<NotFoundResult>(result);
    }
}
