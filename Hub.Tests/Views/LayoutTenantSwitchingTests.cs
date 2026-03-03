using FluentAssertions;

namespace Hub.Tests.Views;

public class LayoutTenantSwitchingTests
{
    private readonly string _layoutContent;

    public LayoutTenantSwitchingTests()
    {
        var solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var layoutPath = Path.Combine(solutionDir, "Views", "Shared", "_Layout.cshtml");
        _layoutContent = File.ReadAllText(layoutPath);
    }

    [Fact]
    public void Layout_DoesNotReference_BootstrapGlobal()
    {
        // The 'bootstrap' global is not available because esbuild wraps Bootstrap's
        // UMD module in a CommonJS factory. Using bootstrap.Dropdown.getInstance()
        // throws a ReferenceError that crashes the tenant switch click handler.
        _layoutContent.Should().NotContain("bootstrap.Dropdown",
            "bootstrap is not a global variable — esbuild wraps it in a CommonJS factory");
    }

    [Fact]
    public void Layout_ClosesDropdown_WithDomManipulation()
    {
        // Dropdown should be closed via DOM classList manipulation, not Bootstrap JS API.
        _layoutContent.Should().Contain("dropdownMenu.classList.remove('show')");
        _layoutContent.Should().Contain("tenantDropdown.classList.remove('show')");
        _layoutContent.Should().Contain("tenantDropdown.setAttribute('aria-expanded', 'false')");
    }

    [Fact]
    public void Layout_ReloadsPage_ForLocalAndStagingTenants()
    {
        // After a successful tenant switch on local/staging, the page must reload
        // so the UI reflects the new tenant. Previously it only hid the spinner.
        _layoutContent.Should().Contain("window.location.reload()");
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

        localStagingBlock.Should().NotBeEmpty("the local/staging branch should exist in the success handler");
        localStagingBlock.Should().NotContain("setTenantLoading(false)",
            "hiding the spinner without reloading leaves stale tenant data on screen");
    }

    private static string ExtractBetween(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        if (startIndex < 0) return string.Empty;

        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        if (endIndex < 0) return string.Empty;

        return source[startIndex..endIndex];
    }
}
