using System.Text.RegularExpressions;

namespace Hub.Tests.Views;

// Guards the DFRNT icon migration (brand book "Iconography - UI"): the app moved
// off the Material Symbols CDN font onto inlined Lucide/Tabler SVGs via the
// <dfrnt-icon> TagHelper. These assertions fail if a Material Symbols reference
// creeps back in or the icon base style is dropped.
public partial class IconSystemTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "wwwroot", "css", "site.less")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Skip("repo root not found relative to test output directory");
        return "";
    }

    private static IEnumerable<string> Views() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Views"), "*.cshtml", SearchOption.AllDirectories);

    [Fact]
    public void Layout_DoesNotLoadMaterialSymbolsFont()
    {
        var layout = File.ReadAllText(Path.Combine(RepoRoot(), "Views", "Shared", "_Layout.cshtml"));
        Assert.DoesNotContain("Material+Symbols", layout);
    }

    [Fact]
    public void NoView_UsesMaterialSymbolsClass()
    {
        foreach (var view in Views())
        {
            Assert.DoesNotContain("material-symbols", File.ReadAllText(view));
        }
    }

    [Fact]
    public void NoView_UsesLigatureMdIcon()
    {
        // e.g. <md-icon>settings</md-icon> — the old font-ligature form. Icons now
        // slot a <dfrnt-icon> child instead.
        foreach (var view in Views())
        {
            Assert.DoesNotMatch(LigatureMdIcon(), File.ReadAllText(view));
        }
    }

    [Fact]
    public void SiteLess_DefinesDfrntIconBase()
    {
        var siteLess = File.ReadAllText(Path.Combine(RepoRoot(), "wwwroot", "css", "site.less"));
        Assert.Matches(@"\.dfrnt-icon\s*\{[^}]*stroke:\s*currentColor", siteLess);
    }

    [GeneratedRegex("<md-icon[^>]*>[a-z_]+</md-icon>")]
    private static partial Regex LigatureMdIcon();
}
