using Hub.Interfaces;
using Hub.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using NSubstitute;

namespace Hub.Tests.ViewComponents;

public class TenantLogoViewComponentTests
{
    private readonly ITenantLogoService _mockLogoService;
    private readonly TenantLogoViewComponent _viewComponent;

    public TenantLogoViewComponentTests()
    {
        _mockLogoService = Substitute.For<ITenantLogoService>();
        _viewComponent = new TenantLogoViewComponent(_mockLogoService);
    }

    [Fact]
    public async Task InvokeAsync_S3Logo_IsS3LogoTrue()
    {
        _mockLogoService.GetLogoUrlAsync().Returns("https://s3.amazonaws.com/bucket/logo.png");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        Assert.True(model!.IsS3Logo);
    }

    [Fact]
    public async Task InvokeAsync_LocalLogo_IsS3LogoFalse()
    {
        _mockLogoService.GetLogoUrlAsync().Returns("/images/logo.png");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        Assert.False(model!.IsS3Logo);
        Assert.Equal("/images/logo.png", model.LogoUrl);
    }

    [Fact]
    public async Task InvokeAsync_NullLogo_UsesFallback()
    {
        _mockLogoService.GetLogoUrlAsync().Returns((string)null!);

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        Assert.Equal("/images/DFRNT_HorizLogo_RGB.png", model!.LogoUrl);
        Assert.False(model.IsS3Logo);
    }

    [Fact]
    public async Task InvokeAsync_CssClassAndAltForwarded()
    {
        _mockLogoService.GetLogoUrlAsync().Returns("/images/logo.png");

        var result = await _viewComponent.InvokeAsync("my-class", "My Logo") as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        Assert.Equal("my-class", model!.CssClass);
        Assert.Equal("My Logo", model.AltText);
    }

    [Fact]
    public async Task InvokeAsync_DefaultsApplied()
    {
        _mockLogoService.GetLogoUrlAsync().Returns("/images/logo.png");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        Assert.Equal("", model!.CssClass);
        Assert.Equal("Company Logo", model.AltText);
    }

    [Fact]
    public async Task InvokeAsync_S3Url_IsUrlEncoded()
    {
        _mockLogoService.GetLogoUrlAsync()
            .Returns("https://s3.amazonaws.com/bucket/logo.png?X-Amz-Security-Token=abc+def");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        Assert.True(model!.IsS3Logo);
        // UrlPathEncode should have been applied
        Assert.False(string.IsNullOrEmpty(model.LogoUrl));
    }
}
