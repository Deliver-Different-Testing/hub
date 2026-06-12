namespace Hub.Tests.Views;

public class LayoutTenantSwitchingTests
{
    private readonly string _layoutContent;
    private readonly string _layoutJsContent;

    public LayoutTenantSwitchingTests()
    {
        var layoutPath = FindFile("Views", "Shared", "_Layout.cshtml");
        if (layoutPath is null)
            Assert.Skip("_Layout.cshtml not found relative to test output directory");
        _layoutContent = File.ReadAllText(layoutPath);

        var layoutJsPath = FindFile("src", "layout.ts");
        if (layoutJsPath is null)
            Assert.Skip("layout.ts not found relative to test output directory");
        _layoutJsContent = File.ReadAllText(layoutJsPath);
    }

    private static string? FindFile(params string[] pathSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. pathSegments]);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Layout_DoesNotReference_BootstrapGlobal()
    {
        // The 'bootstrap' global is not available because esbuild wraps Bootstrap's
        // UMD module in a CommonJS factory. Using bootstrap.Dropdown.getInstance()
        // throws a ReferenceError that crashes the tenant switch click handler.
        // bootstrap is not a global variable — esbuild wraps it in a CommonJS factory
        Assert.DoesNotContain("bootstrap.Dropdown", _layoutContent);
        Assert.DoesNotContain("bootstrap.Dropdown", _layoutJsContent);
    }

    [Fact]
    public void Layout_ClosesDropdown_WithDomManipulation()
    {
        // Dropdown should be closed via DOM classList manipulation, not Bootstrap JS API.
        // Tolerate optional non-null assertions (dropdownMenu!.classList...).
        Assert.Matches(@"dropdownMenu!?\.classList\.remove\('show'\)", _layoutJsContent);
        Assert.Matches(@"tenantDropdown!?\.classList\.remove\('show'\)", _layoutJsContent);
        Assert.Matches(@"tenantDropdown!?\.setAttribute\('aria-expanded',\s*'false'\)", _layoutJsContent);
    }

    [Fact]
    public void Layout_FollowsRedirectUrl_WhenProvidedByBackend()
    {
        // Phase 2: the backend returns a redirectUrl pointing at the destination
        // Hub's AcceptTenantSwitchToken endpoint. The frontend follows it.
        Assert.Contains("data.redirectUrl", _layoutJsContent);
        Assert.Contains("window.location.href = data.redirectUrl", _layoutJsContent);
    }

    [Fact]
    public void Layout_ReloadsAsFallback_WhenNoRedirectUrl()
    {
        // If the backend doesn't return a redirectUrl (older backend or failure to
        // compute destination), fall back to in-place reload so the page at least
        // re-renders against the just-issued cookie on the current subdomain.
        Assert.Contains("window.location.reload()", _layoutJsContent);
    }

    [Fact]
    public void Layout_DoesNotPerformClientSideSubdomainSwap()
    {
        // Cross-subdomain redirection now happens server-side via the SSO redirect URL.
        // Client-side URL-manipulation helpers (which would not carry the destination
        // cookie due to per-tenant Cookie.Domain scoping) must not return.
        Assert.DoesNotContain("replaceTenantNameAndRefresh", _layoutJsContent);
        Assert.DoesNotContain("getCurrentTenantNameFromUrl", _layoutJsContent);
    }

}
