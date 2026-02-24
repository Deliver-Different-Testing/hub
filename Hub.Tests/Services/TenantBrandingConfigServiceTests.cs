using FluentAssertions;
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

        result.Should().NotBeNull();
        result.TenantId.Should().Be(1);
        result.CompanyName.Should().Be("Test Company");
        result.Phone.Should().Be("+64 9 123 4567");
        result.PrimaryColour.Should().Be("#FF0000");
        result.PaperSize.Should().Be("A4");
    }

    [Fact]
    public async Task GetReportConfigAsync_NotFound_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetReportConfigAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReportConfigAsync_FiltersEmptyAddressLines()
    {
        var service = CreateService();

        var result = await service.GetReportConfigAsync(1);

        // AddressLine3 is null in seed data, so should be filtered out
        result!.AddressLines.Should().HaveCount(2);
        result.AddressLines.Should().Contain("123 Test St");
        result.AddressLines.Should().Contain("Suite 100");
    }

    [Fact]
    public async Task GetReportConfigAsync_IncludesLogoUrl()
    {
        var service = CreateService();

        var result = await service.GetReportConfigAsync(1);

        result!.LogoUrl.Should().Be("https://s3.amazonaws.com/logo.png");
    }

    [Fact]
    public async Task GetReportConfigAsync_IncludesTimezoneAndCountry()
    {
        var service = CreateService();

        var result = await service.GetReportConfigAsync(1);

        result!.TimeZoneId.Should().Be("New Zealand Standard Time");
        result.CountryCode.Should().Be("NZ");
    }

    [Fact]
    public async Task GetReportConfigAsync_IncludesBrandingColors()
    {
        var service = CreateService();

        var result = await service.GetReportConfigAsync(1);

        result!.HeaderTextColour.Should().Be("#FFFFFF");
        result.AccentColour.Should().Be("#00FF00");
    }
}
