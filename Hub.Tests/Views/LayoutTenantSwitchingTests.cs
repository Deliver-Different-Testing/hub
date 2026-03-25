namespace Hub.Tests.Views;

public class LayoutTenantSwitchingTests
{
    private readonly string _layoutContent;

    public LayoutTenantSwitchingTests()
    {
        var layoutPath = FindLayoutFile();
        if (layoutPath is null)
            Assert.Skip("_Layout.cshtml not found relative to test output directory");
        _layoutContent = File.ReadAllText(layoutPath);
    }

    private static string? FindLayoutFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Views", "Shared", "_Layout.cshtml");
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
    }

    [Fact]
    public void Layout_ClosesDropdown_WithDomManipulation()
    {
        // Dropdown should be closed via DOM classList manipulation, not Bootstrap JS API.
        Assert.Contains("dropdownMenu.classList.remove('show')", _layoutContent);
        Assert.Contains("tenantDropdown.classList.remove('show')", _layoutContent);
        Assert.Contains("tenantDropdown.setAttribute('aria-expanded', 'false')", _layoutContent);
    }

    [Fact]
    public void Layout_ReloadsPage_ForLocalAndStagingTenants()
    {
        // After a successful tenant switch on local/staging, the page must reload
        // so the UI reflects the new tenant. Previously it only hid the spinner.
        Assert.Contains("window.location.reload()", _layoutContent);
    }

    [Fact]
    public void Layout_DoesNotSilentlySwallowTenantSwitch_OnLocalOrStaging()
    {
        // Extract the local/staging branch inside the .then(data => { ... }) success handler.
        // It should NOT just call setTenantLoading(false) and return — that leaves
        // the page showing stale data from the previous tenant.
        var successHandler = ExtractBetween(_layoutContent, "if (data.success)", ".catch(");
        var localStagingBlock = ExtractBetween(successHandler,
            "if (urlTenantName === 'local' || urlTenantName === 'staging')", "replaceTenantNameAndRefresh");

        // the local/staging branch should exist in the success handler
        Assert.NotEmpty(localStagingBlock);
        // hiding the spinner without reloading leaves stale tenant data on screen
        Assert.DoesNotContain("setTenantLoading(false)", localStagingBlock);
    }

    private static string ExtractBetween(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        if (startIndex < 0) return string.Empty;

        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        return endIndex < 0 ? string.Empty : source[startIndex..endIndex];
    }
}
