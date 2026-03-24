using Hub.Services;
using Hub.Tests.Helpers;
using Moq;

namespace Hub.Tests.Services;

public class TenantBrandingConfigServiceTests
{
    private readonly Mock<ITenantLogoService> _mockLogoService;

    public TenantBrandingConfigServiceTests()
    {
        _mockLogoService = new Mock<ITenantLogoService>();
        _mockLogoService
            .Setup(s => s.GetLogoUrlAsync())
            .ReturnsAsync("https://s3.amazonaws.com/logo.png");
    }

    private TenantBrandingConfigService CreateService()
    {
        var context = TestMasterContextFactory.CreateWithSeedData();
        return new TenantBrandingConfigService(context, _mockLogoService.Object);
    }

    [Fact]
    public async Task GetReportConfigAsync_ExistingTenant_ReturnsFullResponse()
    {
        var service = CreateService();

        var result = await service.GetReportConfigAsync(1);

        Assert.NotNull(result);
        Assert.Equal(1, result.TenantId);
        Assert.Equal("Test Company", result.CompanyName);
        Assert.Equal("+64 9 123 4567", result.Phone);
        Assert.Equal("#FF0000", result.PrimaryColour);
        Assert.Equal("A4", result.PaperSize);
    }

    [Fact]
    public async Task GetReportConfigAsync_NotFound_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetReportConfigAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetReportConfigAsync_FiltersEmptyAddressLines()
    {
        var service = CreateService();

        var result = await service.GetReportConfigAsync(1);

        // AddressLine3 is null in seed data, so should be filtered out
        Assert.Equal(2, result!.AddressLines.Count());
        Assert.Contains("123 Test St", result.AddressLines);
        Assert.Contains("Suite 100", result.AddressLines);
    }

    [Fact]
    public async Task GetReportConfigAsync_IncludesLogoUrl()
    {
        var service = CreateService();

        var result = await service.GetReportConfigAsync(1);

        Assert.Equal("https://s3.amazonaws.com/logo.png", result!.LogoUrl);
    }

    [Fact]
    public async Task GetReportConfigAsync_IncludesTimezoneAndCountry()
    {
        var service = CreateService();

        var result = await service.GetReportConfigAsync(1);

        Assert.Equal("New Zealand Standard Time", result!.TimeZoneId);
        Assert.Equal("NZ", result.CountryCode);
    }

    [Fact]
    public async Task GetReportConfigAsync_IncludesBrandingColors()
    {
        var service = CreateService();

        var result = await service.GetReportConfigAsync(1);

        Assert.Equal("#FFFFFF", result!.HeaderTextColour);
        Assert.Equal("#00FF00", result.AccentColour);
    }
}
