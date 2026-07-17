using System.Text.RegularExpressions;

namespace Hub.Tests.Views;

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

    private string TenantDropdownBlock()
    {
        // Grab the `#tenantDropdown { ... }` rule body. The block only nests one
        // level (.spinner-border), so a balanced match to the first closing
        // brace at column-0-ish is unnecessary — capture up to the closing brace
        // that ends the top-level block.
        var match = TenantDropdownRegex().Match(_siteLess);
        Assert.True(match.Success, "#tenantDropdown rule not found in site.less");
        return match.Groups["body"].Value;
    }

    [Fact]
    public void TenantDropdown_LabelText_UsesOnSurfaceNotOnPrimary()
    {
        // Blue theme's --md-sys-color-on-primary is white; the filled tenant
        // button must instead use the on-surface (black) token so its text
        // matches the adjacent black profile trigger text.
        var block = TenantDropdownBlock();

        Assert.Matches(
            @"--md-filled-button-label-text-color\s*:\s*var\(--dd-on-surface\)",
            block);
    }

    [Fact]
    public void TenantDropdown_IconAndInteractionStates_AlsoUseOnSurface()
    {
        // Every state resolves independently and falls back to on-primary, so
        // hover/focus/pressed (label + icon) must be pinned too, or the text
        // flashes white on interaction.
        var block = TenantDropdownBlock();

        string[] tokens =
        [
            "--md-filled-button-hover-label-text-color",
            "--md-filled-button-focus-label-text-color",
            "--md-filled-button-pressed-label-text-color",
            "--md-filled-button-icon-color",
            "--md-filled-button-hover-icon-color",
            "--md-filled-button-focus-icon-color",
            "--md-filled-button-pressed-icon-color",
        ];

        foreach (var token in tokens)
        {
            Assert.Matches($@"{Regex.Escape(token)}\s*:\s*var\(--dd-on-surface\)", block);
        }
    }

    // Anchor to the top-level (column-0) navbar rule so the match isn't stolen by
    // the indented `#tenantDropdown` override nested inside the dark-mode scheme.
    [GeneratedRegex(@"^#tenantDropdown\s*\{(?<body>.*?)^\}", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex TenantDropdownRegex();
}
