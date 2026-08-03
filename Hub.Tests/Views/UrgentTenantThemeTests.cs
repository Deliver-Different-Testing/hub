using System.Text.RegularExpressions;

namespace Hub.Tests.Views;

// Guards the Urgent tenant's reintroduced gold brand primary (the theming
// skill's urgentPrimaryPalette, #f4c430). DFRNT stays the universal brand; only
// the urgent tenant repaints, via a body[data-tenant-code="urgent"] override in
// site.less that flips ONLY the --dd-primary* block. Cyan (--dd-accent) and the
// neutral/surface tokens must stay DFRNT, and the override depends on the
// data-tenant-code attribute still being emitted on <body>.
public partial class UrgentTenantThemeTests
{
    private readonly string _siteLess = Read("wwwroot", "css", "site.less");

    private static string Read(params string[] segments)
    {
        var path = FindFile(segments);
        if (path is null)
        {
            Assert.Skip($"{string.Join('/', segments)} not found relative to test output directory");
        }

        return File.ReadAllText(path);
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

    private string UrgentOverrideBlock()
    {
        var match = UrgentOverrideRegex().Match(_siteLess);
        Assert.True(match.Success, "body[data-tenant-code=\"urgent\"] override not found in site.less");
        return match.Groups["b"].Value;
    }

    [Fact]
    public void UrgentOverride_SetsGoldPrimary()
    {
        var block = UrgentOverrideBlock();
        Assert.Matches(@"--dd-primary\s*:\s*#f4c430", block);
        Assert.Matches(@"--dd-primary-rgb\s*:\s*244,\s*196,\s*48", block);
    }

    [Fact]
    public void UrgentOverride_UsesDarkInkTextOnGold()
    {
        // Gold needs dark text to stay legible (the cyan/reflex-blue on-primary
        // white would fail on it), so the override carries dark Ink on-primary.
        var block = UrgentOverrideBlock();
        Assert.Matches(@"--dd-on-primary\s*:\s*#1c1917", block);
        Assert.Matches(@"--dd-on-primary-rgb\s*:\s*28,\s*25,\s*23", block);
    }

    [Fact]
    public void UrgentOverride_PaintsAppBarGold()
    {
        // The navbar paints from --dd-ink (background) + --dd-on-ink (chrome), so
        // the urgent bar goes gold only if the override flips both — to gold with
        // dark Ink chrome that reads on it.
        var block = UrgentOverrideBlock();
        Assert.Matches(@"--dd-ink\s*:\s*#f4c430", block);
        Assert.Matches(@"--dd-on-ink\s*:\s*#1c1917", block);
    }

    [Fact]
    public void UrgentOverride_UsesDarkInkLogoOnGoldBar()
    {
        // The gold app bar is a light surface, so the urgent navbar must reverse
        // the base swap and show the dark-ink artwork (.logo-light), hiding the
        // white-wordmark (.logo-dark) variant the Ink Blue bar uses.
        Assert.Matches(
            @"body\[data-tenant-code=""urgent""\]\s*\.navbar\s*\.logo\.logo-light\s*\{[^}]*display:\s*block",
            _siteLess);
        Assert.Matches(
            @"body\[data-tenant-code=""urgent""\]\s*\.navbar\s*\.logo\.logo-dark\s*\{[^}]*display:\s*none",
            _siteLess);
    }

    [Fact]
    public void UrgentOverride_LeavesAccentAndSurfacesDfrnt()
    {
        // The override is primary-only: it must NOT touch the Cyan CTA accent or
        // any neutral/surface token — those stay DFRNT for every tenant.
        var block = UrgentOverrideBlock();
        Assert.DoesNotMatch(@"--dd-accent\b", block);
        Assert.DoesNotMatch(@"--dd-surface", block);
        Assert.DoesNotMatch(@"--dd-on-surface", block);
    }

    [Fact]
    public void Layout_EmitsTenantCodeHook()
    {
        // The override keys off the data-tenant-code attribute; if the layout
        // stops emitting it, the gold theme silently never applies.
        var layout = Read("Views", "Shared", "_Layout.cshtml");
        Assert.Contains("data-tenant-code=\"@currentTenantCode\"", layout);
    }

    // Captures the body[data-tenant-code="urgent"] { ... } block body.
    [GeneratedRegex(@"body\[data-tenant-code=""urgent""\]\s*\{(?<b>[^}]*)\}")]
    private static partial Regex UrgentOverrideRegex();
}
