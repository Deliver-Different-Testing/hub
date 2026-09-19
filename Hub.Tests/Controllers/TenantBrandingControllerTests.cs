using Hub.Controllers;
using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Controllers;

public class TenantBrandingControllerTests
{
    private readonly ITenantBrandingConfigService _mockService;
    private readonly TenantBrandingController _controller;

    public TenantBrandingControllerTests()
    {
        _mockService = Substitute.For<ITenantBrandingConfigService>();
        _controller = new TenantBrandingController(_mockService);
    }

    [Fact]
    public async Task GetReportConfig_ExistingTenant_Returns200()
    {
        _mockService.GetReportConfigAsync(1)
            .Returns(new TenantBrandingResponse
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
        _mockService.GetReportConfigAsync(999)
            .Returns((TenantBrandingResponse?)null);

        var result = await _controller.GetReportConfig(999);

        Assert.IsType<NotFoundResult>(result);
    }
}
