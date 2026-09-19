using System.Security.Claims;
using Hub.Interfaces;
using Hub.ViewComponents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    public async Task InvokeAsync_LocalLogo_DerivesDarkVariantUrl()
    {
        _mockLogoService.GetLogoUrlAsync().Returns("/images/logo.png");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        // Local logos ship a "_dark" light-wordmark variant for the dark theme swap.
        Assert.Equal("/images/logo_dark.png", model!.DarkLogoUrl);
    }

    [Fact]
    public async Task InvokeAsync_S3Logo_HasNoDarkVariant()
    {
        _mockLogoService.GetLogoUrlAsync().Returns("https://s3.amazonaws.com/bucket/logo.png");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        // Arbitrary third-party art can't be recoloured, so no swap variant exists.
        Assert.Null(model!.DarkLogoUrl);
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
    public async Task InvokeAsync_UrgentTenant_FallbackIsUrgentLocalLogo()
    {
        _mockLogoService.GetLogoUrlAsync().Returns("https://s3.amazonaws.com/bucket/logo.png");
        SetTenantCode("urgent");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        // On S3 load failure the urgent tenant falls back to its own brand art.
        Assert.Equal("/images/urgentCouriersNewLogo.png", model!.FallbackLogoUrl);
    }

    [Fact]
    public async Task InvokeAsync_UrgentTenant_FallbackIsCaseInsensitive()
    {
        _mockLogoService.GetLogoUrlAsync().Returns("https://s3.amazonaws.com/bucket/logo.png");
        SetTenantCode("URGENT");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        Assert.Equal("/images/urgentCouriersNewLogo.png", model!.FallbackLogoUrl);
    }

    [Fact]
    public async Task InvokeAsync_NonUrgentTenant_FallbackIsDfrntDefault()
    {
        _mockLogoService.GetLogoUrlAsync().Returns("https://s3.amazonaws.com/bucket/logo.png");
        SetTenantCode("someothertenant");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        Assert.Equal("/images/DFRNT_HorizLogo_RGB.png", model!.FallbackLogoUrl);
    }

    [Fact]
    public async Task InvokeAsync_NoTenantContext_FallbackIsDfrntDefault()
    {
        // No ViewComponentContext set (as the other tests) — the tenant read must
        // stay null-safe and default to the DFRNT fallback rather than throw.
        _mockLogoService.GetLogoUrlAsync().Returns("https://s3.amazonaws.com/bucket/logo.png");

        var result = await _viewComponent.InvokeAsync() as ViewViewComponentResult;

        var model = result!.ViewData!.Model as TenantLogoViewComponent.TenantLogoViewModel;
        Assert.Equal("/images/DFRNT_HorizLogo_RGB.png", model!.FallbackLogoUrl);
    }

    private void SetTenantCode(string tenantCode)
    {
        var identity = new ClaimsIdentity([new Claim("TenantCode", tenantCode)], "TestAuth");
        _viewComponent.ViewComponentContext = new ViewComponentContext
        {
            ViewContext = new ViewContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
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
