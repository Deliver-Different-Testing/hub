using Hub.Controllers;
using Hub.Services;
using Hub.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Hub.Tests.Controllers;

public class LogoControllerTests
{
    private readonly Mock<ITenantLogoService> _mockLogoService;
    private readonly LogoController _controller;

    public LogoControllerTests()
    {
        _mockLogoService = new Mock<ITenantLogoService>();
        _controller = new LogoController(_mockLogoService.Object);
    }

    // GetLogo tests
    [Fact]
    public async Task GetLogo_LocalPath_ReturnsIsLocalTrue()
    {
        _mockLogoService.Setup(s => s.GetLogoUrlAsync()).ReturnsAsync("/images/logo.png");

        var result = await _controller.GetLogo() as JsonResult;

        var value = result!.Value;
        AssertHelper.JsonEquivalent(new { success = true, logoUrl = "/images/logo.png", isLocal = true }, value);
    }

    [Fact]
    public async Task GetLogo_S3Url_ReturnsIsLocalFalse()
    {
        _mockLogoService.Setup(s => s.GetLogoUrlAsync()).ReturnsAsync("https://s3.amazonaws.com/logo.png");

        var result = await _controller.GetLogo() as JsonResult;

        var value = result!.Value;
        AssertHelper.JsonEquivalent(new { success = true, logoUrl = "https://s3.amazonaws.com/logo.png", isLocal = false }, value);
    }

    [Fact]
    public async Task GetLogo_NullUrl_ReturnsIsLocalFalse()
    {
        _mockLogoService.Setup(s => s.GetLogoUrlAsync()).ReturnsAsync((string)null!);

        var result = await _controller.GetLogo() as JsonResult;

        var value = result!.Value;
        AssertHelper.JsonEquivalent(new { success = true, logoUrl = (string?)null, isLocal = false }, value);
    }

    [Fact]
    public async Task GetLogo_ServiceThrows_ReturnsFailure()
    {
        _mockLogoService.Setup(s => s.GetLogoUrlAsync()).ThrowsAsync(new Exception("S3 error"));

        var result = await _controller.GetLogo() as JsonResult;

        var value = result!.Value;
        AssertHelper.JsonEquivalent(new { success = false, message = "Error retrieving logo", logoUrl = "/images/DFRNT_HorizLogo_RGB.png" }, value);
    }

    // LogoExists tests
    [Fact]
    public async Task LogoExists_Exists_ReturnsTrue()
    {
        _mockLogoService.Setup(s => s.LogoExistsAsync()).ReturnsAsync(true);

        var result = await _controller.LogoExists() as JsonResult;

        AssertHelper.JsonEquivalent(new { success = true, exists = true }, result!.Value);
    }

    [Fact]
    public async Task LogoExists_NotExists_ReturnsFalse()
    {
        _mockLogoService.Setup(s => s.LogoExistsAsync()).ReturnsAsync(false);

        var result = await _controller.LogoExists() as JsonResult;

        AssertHelper.JsonEquivalent(new { success = true, exists = false }, result!.Value);
    }

    [Fact]
    public async Task LogoExists_Throws_ReturnsFailure()
    {
        _mockLogoService.Setup(s => s.LogoExistsAsync()).ThrowsAsync(new Exception("error"));

        var result = await _controller.LogoExists() as JsonResult;

        AssertHelper.JsonEquivalent(new { success = false, exists = false }, result!.Value);
    }

    // ClearCache tests
    [Fact]
    public void ClearCache_Success_ReturnsOk()
    {
        var result = _controller.ClearCache() as JsonResult;

        AssertHelper.JsonEquivalent(new { success = true, message = "Cache cleared successfully" }, result!.Value);
    }

    [Fact]
    public void ClearCache_Throws_ReturnsFailure()
    {
        _mockLogoService.Setup(s => s.ClearCache()).Throws(new Exception("error"));

        var result = _controller.ClearCache() as JsonResult;

        AssertHelper.JsonEquivalent(new { success = false, message = "Error clearing cache" }, result!.Value);
    }
}
