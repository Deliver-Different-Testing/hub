using Hub.Controllers;
using Hub.Features.Branding;
using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Controllers;

public class TenantBrandingControllerTests
{
    private readonly ITenantBrandingConfigService _mockService;
    private readonly ITenantAppearanceService _appearanceService;
    private readonly TenantBrandingController _controller;

    public TenantBrandingControllerTests()
    {
        _mockService = Substitute.For<ITenantBrandingConfigService>();
        _appearanceService = Substitute.For<ITenantAppearanceService>();
        _controller = new TenantBrandingController(_mockService, _appearanceService);
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

    [Fact]
    public async Task GetAppearance_Found_Returns200WithCleanedObject()
    {
        var appearance = new TenantAppearance("high", "#2a4eff", null, null, null);
        _appearanceService.GetAsync(1, Arg.Any<CancellationToken>()).Returns(TenantAppearanceLookup.Found(appearance));

        var result = await _controller.GetAppearance(1, TestContext.Current.CancellationToken);

        Assert.Same(appearance, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task GetAppearance_NothingConfigured_Returns204()
    {
        _appearanceService.GetAsync(1, Arg.Any<CancellationToken>()).Returns(TenantAppearanceLookup.NoneConfigured);

        Assert.IsType<NoContentResult>(await _controller.GetAppearance(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAppearance_UnknownTenant_Returns404()
    {
        _appearanceService.GetAsync(9, Arg.Any<CancellationToken>()).Returns(TenantAppearanceLookup.NotFound);

        Assert.IsType<NotFoundResult>(await _controller.GetAppearance(9, TestContext.Current.CancellationToken));
    }
}
