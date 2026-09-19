using System.Text.RegularExpressions;

namespace Hub.Tests.Views;

// Guards the tenant-selector button chrome after the Bootstrap migration. The
// organisation selector is a `.btn.btn-primary.dropdown-toggle` sitting on the
// constant dark Ink Blue navbar, so its label + caret must use the light
// --dd-on-ink chrome token (not the default on-primary, and not the dark
// on-surface Ink text) or it becomes unreadable on the bar.
public partial class TenantButtonTextColorTests
{
    private readonly string _siteLess;

    public TenantButtonTextColorTests()
    {
        var path = FindFile("wwwroot", "css", "site.less");
        if (path is null)
        {
            Assert.Skip("site.less not found relative to test output directory");
        }

        _siteLess = File.ReadAllText(path);
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

    private string TenantToggleBlock()
    {
        var match = TenantToggleRegex().Match(_siteLess);
        Assert.True(match.Success, ".tenant-selector .btn-primary.dropdown-toggle rule not found in site.less");
        return match.Groups["body"].Value;
    }

    [Fact]
    public void TenantToggle_LabelText_UsesOnInk()
    {
        // The tenant button sits on the constant dark Ink Blue bar, so its label
        // must use the light --dd-on-ink chrome token.
        var block = TenantToggleBlock();
        Assert.Matches(@"color\s*:\s*var\(--dd-on-ink\)", block);
    }

    [Fact]
    public void TenantToggle_Caret_UsesOnInk()
    {
        // Bootstrap's dropdown caret (::after) must also be on-ink, or it renders
        // as a dark triangle on the dark bar.
        var block = TenantToggleBlock();
        Assert.Matches(@"&::after\s*\{[^}]*color\s*:\s*var\(--dd-on-ink\)", block);
    }

    [Fact]
    public void TenantToggle_Fill_IsTranslucentOnInkChip()
    {
        // A translucent on-primary/on-ink overlay reads as a lighter chip on the
        // dark bar (the @background_color_13 var), not a solid Reflex-Blue blob.
        var block = TenantToggleBlock();
        Assert.Matches(@"background-color\s*:\s*@background_color_13", block);
        Assert.Matches(@"@background_color_13\s*:\s*rgba\(var\(--dd-on-primary-rgb\)", _siteLess);
    }

    // The tenant button rule is nested under `.tenant-selector { ... }`.
    [GeneratedRegex(@"\.btn-primary\.dropdown-toggle\s*\{(?<body>.*?)\n  \}", RegexOptions.Singleline)]
    private static partial Regex TenantToggleRegex();
}
