namespace Hub.Tests.Views;

public class LayoutTenantSwitchingTests
{
    private readonly string _layoutContent;
    private readonly string _layoutJsContent;

    public LayoutTenantSwitchingTests()
    {
        var layoutPath = FindFile("Views", "Shared", "_Layout.cshtml");
        if (layoutPath is null)
        {
            Assert.Skip("_Layout.cshtml not found relative to test output directory");
        }

        _layoutContent = File.ReadAllText(layoutPath);

        var layoutJsPath = FindFile("src", "layout.ts");
        if (layoutJsPath is null)
        {
            Assert.Skip("layout.ts not found relative to test output directory");
        }

        _layoutJsContent = File.ReadAllText(layoutJsPath);
    }

    private static string? FindFile(params string[] pathSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. pathSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

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
    public void Layout_UsesBootstrapDropdown_ForMenus()
    {
        // The menus are now Bootstrap dropdowns: the triggers declare
        // data-bs-toggle="dropdown" (Bootstrap's JS manages open/close/anchor/
        // outside-click/Escape) and the tenant options are .dropdown-item buttons
        // carrying data-tenant-id. The old <md-menu>/<md-menu-item> markup and the
        // imperative `menu.open` toggling are gone.
        Assert.Contains("data-bs-toggle=\"dropdown\"", _layoutContent);
        Assert.DoesNotContain("<md-menu", _layoutContent);
        Assert.Contains("class=\"dropdown-item", _layoutContent);
        Assert.Contains("data-tenant-code=", _layoutContent);
        Assert.Contains("data-tenant-id=", _layoutContent);

        // layout.ts reacts to .dropdown-item clicks, not md-menu-item, and no
        // longer imperatively toggles an md-menu `open` property.
        Assert.Contains(".dropdown-item", _layoutJsContent);
        Assert.DoesNotContain("md-menu-item", _layoutJsContent);
        Assert.DoesNotMatch(@"menu!?\.open", _layoutJsContent);
    }

    [Fact]
    public void ThemeToggle_ButtonIsDropdownSibling_NotWrappedInTooltipSpan()
    {
        // Regression: the theme toggle button must be a direct child of .dropdown
        // (sibling of #themeMenu) so Bootstrap can resolve the menu. Wrapping the
        // toggle in a <span class="dd-tooltip"> hid the menu from Bootstrap's
        // sibling/parent lookup, so _isShown() dereferenced a null _menu and the
        // menu wouldn't open. The tooltip classes therefore live ON the button.
        Assert.Matches(
            @"<button[^>]*class=""[^""]*dd-tooltip[^""]*""[^>]*id=""themeToggle""[^>]*data-bs-toggle=""dropdown""",
            _layoutContent);
        Assert.DoesNotContain("<span class=\"dd-tooltip\"", _layoutContent);
    }

    [Fact]
    public void ProfileChevron_RotatesViaBootstrapShowClass()
    {
        // Bootstrap toggles `.show` on the dropdown trigger while open, so the
        // profile chevron rotation is driven by CSS off that class — no JS
        // opened/closed bookkeeping.
        var siteLessPath = FindFile("wwwroot", "css", "site.less");
        Assert.NotNull(siteLessPath);
        var siteLess = File.ReadAllText(siteLessPath!);
        Assert.Matches(
            @"\.profile-icon-username\.show\s*\{[\s\S]*?\.dropdown-arrow\s*\{[^}]*transform:\s*rotate\(180deg\)",
            siteLess);
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
