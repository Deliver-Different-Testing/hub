using Hub.Controllers;
using Hub.Interfaces;
using Hub.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Hub.Tests.Controllers;

public class LogoControllerTests
{
    private readonly ITenantLogoService _logoService;
    private readonly LogoController _controller;

    public LogoControllerTests()
    {
        _logoService = Substitute.For<ITenantLogoService>();
        _controller = new LogoController(_logoService);
    }

    // GetLogo tests
    [Fact]
    public async Task GetLogo_LocalPath_ReturnsIsLocalTrue()
    {
        _logoService.GetLogoUrlAsync().Returns("/images/logo.png");

        var result = await _controller.GetLogo() as JsonResult;

        var value = result!.Value;
        AssertHelper.JsonEquivalent(new { success = true, logoUrl = "/images/logo.png", isLocal = true }, value);
    }

    [Fact]
    public async Task GetLogo_S3Url_ReturnsIsLocalFalse()
    {
        _logoService.GetLogoUrlAsync().Returns("https://s3.amazonaws.com/logo.png");

        var result = await _controller.GetLogo() as JsonResult;

        var value = result!.Value;
        AssertHelper.JsonEquivalent(new { success = true, logoUrl = "https://s3.amazonaws.com/logo.png", isLocal = false }, value);
    }

    [Fact]
    public async Task GetLogo_NullUrl_ReturnsIsLocalFalse()
    {
        _logoService.GetLogoUrlAsync().Returns((string)null!);

        var result = await _controller.GetLogo() as JsonResult;

        var value = result!.Value;
        AssertHelper.JsonEquivalent(new { success = true, logoUrl = (string?)null, isLocal = false }, value);
    }

    [Fact]
    public async Task GetLogo_ServiceThrows_ReturnsFailure()
    {
        _logoService.GetLogoUrlAsync().ThrowsAsync(new Exception("S3 error"));

        var result = await _controller.GetLogo() as JsonResult;

        var value = result!.Value;
        AssertHelper.JsonEquivalent(new { success = false, message = "Error retrieving logo", logoUrl = "/images/DFRNT_HorizLogo_RGB.png" }, value);
    }

    // LogoExists tests
    [Fact]
    public async Task LogoExists_Exists_ReturnsTrue()
    {
        _logoService.LogoExistsAsync().Returns(true);

        var result = await _controller.LogoExists() as JsonResult;

        AssertHelper.JsonEquivalent(new { success = true, exists = true }, result!.Value);
    }

    [Fact]
    public async Task LogoExists_NotExists_ReturnsFalse()
    {
        _logoService.LogoExistsAsync().Returns(false);

        var result = await _controller.LogoExists() as JsonResult;

        AssertHelper.JsonEquivalent(new { success = true, exists = false }, result!.Value);
    }

    [Fact]
    public async Task LogoExists_Throws_ReturnsFailure()
    {
        _logoService.LogoExistsAsync().ThrowsAsync(new Exception("error"));

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
        _logoService.When(s => s.ClearCache()).Throw(new Exception("error"));

        var result = _controller.ClearCache() as JsonResult;

        AssertHelper.JsonEquivalent(new { success = false, message = "Error clearing cache" }, result!.Value);
    }
}
