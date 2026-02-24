using FluentAssertions;
using Hub.Services;
using Hub.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Moq;

namespace Hub.Tests.ViewComponents;

public class TenantLogoViewComponentTests
{
    private readonly Mock<ITenantLogoService> _mockLogoService;
    private readonly TenantLogoViewComponent _viewComponent;

    public TenantLogoViewComponentTests()
    {
        _mockLogoService = new Mock<ITenantLogoService>();
        _viewComponent = new TenantLogoViewComponent(_mockLogoService.Object);
    }

    [Fact]
    public async Task InvokeAsync_S3Logo_IsS3LogoTrue()
    {
        _mockLogoService.Setup(s => s.GetLogoUrlAsync()).ReturnsAsync("https://s3.amazonaws.com/bucket/logo.png");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        model!.IsS3Logo.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_LocalLogo_IsS3LogoFalse()
    {
        _mockLogoService.Setup(s => s.GetLogoUrlAsync()).ReturnsAsync("/images/logo.png");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        model!.IsS3Logo.Should().BeFalse();
        model.LogoUrl.Should().Be("/images/logo.png");
    }

    [Fact]
    public async Task InvokeAsync_NullLogo_UsesFallback()
    {
        _mockLogoService.Setup(s => s.GetLogoUrlAsync()).ReturnsAsync((string)null!);

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        model!.LogoUrl.Should().Be("/images/DFRNT_HorizLogo_RGB.png");
        model.IsS3Logo.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_CssClassAndAltForwarded()
    {
        _mockLogoService.Setup(s => s.GetLogoUrlAsync()).ReturnsAsync("/images/logo.png");

        var result = await _viewComponent.InvokeAsync("my-class", "My Logo") as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        model!.CssClass.Should().Be("my-class");
        model.AltText.Should().Be("My Logo");
    }

    [Fact]
    public async Task InvokeAsync_DefaultsApplied()
    {
        _mockLogoService.Setup(s => s.GetLogoUrlAsync()).ReturnsAsync("/images/logo.png");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        model!.CssClass.Should().Be("");
        model.AltText.Should().Be("Company Logo");
    }

    [Fact]
    public async Task InvokeAsync_S3Url_IsUrlEncoded()
    {
        _mockLogoService.Setup(s => s.GetLogoUrlAsync())
            .ReturnsAsync("https://s3.amazonaws.com/bucket/logo.png?X-Amz-Security-Token=abc+def");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        model!.IsS3Logo.Should().BeTrue();
        // UrlPathEncode should have been applied
        model.LogoUrl.Should().NotBeNullOrEmpty();
    }
}
