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
    public void Layout_TogglesMenu_ViaMdMenuOpenProperty()
    {
        // The dropdown is now an MD3 md-menu that manages its own open/close
        // (outside-click / Escape) — no Bootstrap JS API and no manual
        // classList('show')/aria-expanded manipulation. The trigger toggles it
        // imperatively via the `open` property. Tolerate optional non-null
        // assertions (menu!.open ...).
        Assert.Matches(@"menu!?\.open\s*=\s*!menu!?\.open", _layoutJsContent);

        // Stale Bootstrap-era DOM manipulation must not return.
        Assert.DoesNotContain("classList.remove('show')", _layoutJsContent);
        Assert.DoesNotContain("aria-expanded", _layoutJsContent);
    }

    [Fact]
    public void Layout_SyncsTriggerState_WithMenuOpenedClosedEvents()
    {
        // md-menu closes itself; the trigger mirrors its open state via the
        // component's 'opened'/'closed' events so the chevron (.active) stays
        // in sync rather than being driven by manual toggle bookkeeping.
        Assert.Matches(@"addEventListener\('opened',.*classList\.add\('active'\)", _layoutJsContent);
        Assert.Matches(@"addEventListener\('closed',.*classList\.remove\('active'\)", _layoutJsContent);
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
