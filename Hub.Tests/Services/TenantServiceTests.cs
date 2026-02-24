using FluentAssertions;
using Hub.Repositories;
using Hub.Services;
using Hub.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace Hub.Tests.Services;

public class TenantServiceTests
{
    private readonly Mock<IWebHostEnvironment> _mockHostEnv;

    public TenantServiceTests()
    {
        _mockHostEnv = new Mock<IWebHostEnvironment>();
        _mockHostEnv.Setup(e => e.WebRootPath).Returns(Path.GetTempPath());
    }

    private (TenantService service, AuthenticationRepository authRepo) CreateService()
    {
        var context = TestMasterContextFactory.CreateWithSeedData();
        var authRepo = new AuthenticationRepository(context);
        var service = new TenantService(authRepo, _mockHostEnv.Object);
        return (service, authRepo);
    }

    [Fact]
    public async Task GetTenantsForUserAsync_WithTenants_ReturnsTenants()
    {
        var (service, _) = CreateService();

        var tenants = await service.GetTenantsForUserAsync(1);

        tenants.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTenantsForUserAsync_NoTenants_ReturnsEmpty()
    {
        var (service, _) = CreateService();

        var tenants = await service.GetTenantsForUserAsync(999);

        tenants.Should().BeEmpty();
    }

    [Fact]
    public void GetTenantLogoPath_LogoExists_ReturnsTenantPath()
    {
        // Create a temp logo file
        var logoDir = Path.Combine(Path.GetTempPath(), "images");
        Directory.CreateDirectory(logoDir);
        var logoPath = Path.Combine(logoDir, "testLogo.png");
        File.WriteAllText(logoPath, "fake logo");

        try
        {
            var (service, _) = CreateService();

            var result = service.GetTenantLogoPath("test");

            result.Should().Be("~/images/testLogo.png");
        }
        finally
        {
            File.Delete(logoPath);
        }
    }

    [Fact]
    public void GetTenantLogoPath_LogoNotExists_ReturnsDefault()
    {
        var (service, _) = CreateService();

        var result = service.GetTenantLogoPath("nonexistent");

        result.Should().Be("~/images/DFRNT_HorizLogo_RGB.png");
    }

    [Fact]
    public void GetTenantLogoPath_NullCode_ReturnsDefault()
    {
        var (service, _) = CreateService();

        var result = service.GetTenantLogoPath(null!);

        result.Should().Be("~/images/DFRNT_HorizLogo_RGB.png");
    }

    [Fact]
    public void GetTenantLogoPath_EmptyCode_ReturnsDefault()
    {
        var (service, _) = CreateService();

        var result = service.GetTenantLogoPath(string.Empty);

        result.Should().Be("~/images/DFRNT_HorizLogo_RGB.png");
    }
}
