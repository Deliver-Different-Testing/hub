using Hub.Repositories;
using Hub.Services;
using Hub.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using NSubstitute;

namespace Hub.Tests.Services;

public class TenantServiceTests
{
    private readonly IWebHostEnvironment _mockHostEnv;

    public TenantServiceTests()
    {
        _mockHostEnv = Substitute.For<IWebHostEnvironment>();
        _mockHostEnv.WebRootPath.Returns(Path.GetTempPath());
    }

    private (TenantService service, AuthenticationRepository authRepo) CreateService()
    {
        var context = TestMasterContextFactory.CreateWithSeedData();
        var authRepo = new AuthenticationRepository(context);
        var service = new TenantService(authRepo, _mockHostEnv);
        return (service, authRepo);
    }

    [Fact]
    public async Task GetTenantsForUserAsync_WithTenants_ReturnsTenants()
    {
        var service = CreateService().service;

        var tenants = await service.GetTenantsForUserAsync(1);

        Assert.Equal(2, tenants.Count);
    }

    [Fact]
    public async Task GetTenantsForUserAsync_NoTenants_ReturnsEmpty()
    {
        var service = CreateService().service;

        var tenants = await service.GetTenantsForUserAsync(999);

        Assert.Empty(tenants);
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
            var service = CreateService().service;

            var result = service.GetTenantLogoPath("test");

            Assert.Equal("~/images/testLogo.png", result);
        }
        finally
        {
            File.Delete(logoPath);
        }
    }

    [Fact]
    public void GetTenantLogoPath_LogoNotExists_ReturnsDefault()
    {
        var service = CreateService().service;

        var result = service.GetTenantLogoPath("nonexistent");

        Assert.Equal("~/images/DFRNT_HorizLogo_RGB.png", result);
    }

    [Fact]
    public void GetTenantLogoPath_NullCode_ReturnsDefault()
    {
        var service = CreateService().service;

        var result = service.GetTenantLogoPath(null!);

        Assert.Equal("~/images/DFRNT_HorizLogo_RGB.png", result);
    }

    [Fact]
    public void GetTenantLogoPath_EmptyCode_ReturnsDefault()
    {
        var service = CreateService().service;

        var result = service.GetTenantLogoPath(string.Empty);

        Assert.Equal("~/images/DFRNT_HorizLogo_RGB.png", result);
    }

    [Fact]
    public async Task GetCurrentTenantTimeAsync_ValidTenant_ReturnsConvertedTime()
    {
        var service = CreateService().service;

        var result = await service.GetCurrentTenantTimeAsync(1);

        // Tenant 1 has "New Zealand Standard Time" which is UTC+12/+13
        // The converted time should differ from UTC
        var utcNow = DateTime.UtcNow;
        Assert.NotEqual(utcNow.Hour, result.Hour);
    }

    [Fact]
    public async Task GetCurrentTenantTimeAsync_UnknownTenant_FallsBackToUtc()
    {
        var service = CreateService().service;

        // Tenant 999 doesn't exist, GetTenantTimeZoneAsync returns null, falls back to "UTC"
        var result = await service.GetCurrentTenantTimeAsync(999);

        var utcNow = DateTime.UtcNow;
        Assert.True(Math.Abs((result - utcNow).TotalSeconds) < 5);
    }
}
