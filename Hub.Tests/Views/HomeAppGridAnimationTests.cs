namespace Hub.Tests.Views;

public class HomeAppGridAnimationTests
{
    private readonly string _homeView;
    private readonly string _siteLess;

    public HomeAppGridAnimationTests()
    {
        var viewPath = FindFile("Views", "Home", "Index.cshtml");
        if (viewPath is null)
        {
            Assert.Skip("Views/Home/Index.cshtml not found relative to test output directory");
        }

        _homeView = File.ReadAllText(viewPath);

        var lessPath = FindFile("wwwroot", "css", "site.less");
        if (lessPath is null)
        {
            Assert.Skip("wwwroot/css/site.less not found relative to test output directory");
        }

        _siteLess = File.ReadAllText(lessPath);
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
    public void Animation_Covers_SettingsCard_InInternalTenantGrid()
    {
        // The internal-tenant branch (Index.cshtml else block) renders 15 cards
        // ending in Settings. Without a :nth-child(15) animation-delay rule the
        // Settings tile pops in alongside the first card instead of trailing
        // the staggered entry — visually inconsistent with the rest.
        var settingsPosition = SettingsCardPositionInInternalBranch(_homeView);
        Assert.True(settingsPosition > 0, "Settings card not found in home view");

        var highestDelayedChild = HighestNthChildDelayInAppGrid(_siteLess);
        Assert.True(
            highestDelayedChild >= settingsPosition,
            $"site.less staggers up to :nth-child({highestDelayedChild}) but Settings is card #{settingsPosition}.");
    }

    private static int SettingsCardPositionInInternalBranch(string view)
    {
        // The else block starts after the closing brace of the `!clientInternal`
        // branch. Conditional cards inside the else block (e.g. Fuel Surcharge)
        // count toward the position because they may render before Settings.
        var elseStart = view.IndexOf("else\r\n    {", StringComparison.Ordinal);
        if (elseStart < 0)
        {
            elseStart = view.IndexOf("else\n    {", StringComparison.Ordinal);
        }

        if (elseStart < 0)
        {
            return -1;
        }

        var settingsIndex = view.IndexOf("Settings</div>", elseStart, StringComparison.Ordinal);
        if (settingsIndex < 0)
        {
            return -1;
        }

        var count = 0;
        var index = elseStart;
        const string marker = "class=\"app-card\"";
        while ((index = view.IndexOf(marker, index, StringComparison.Ordinal)) >= 0 && index < settingsIndex)
        {
            count++;
            index += marker.Length;
        }
        return count;
    }

    private static int HighestNthChildDelayInAppGrid(string less)
    {
        var highest = 0;
        var index = 0;
        const string marker = "&:nth-child(";
        while ((index = less.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            var start = index + marker.Length;
            var end = less.IndexOf(')', start);
            if (end > start && int.TryParse(less.AsSpan(start, end - start), out var n) && n > highest)
            {
                highest = n;
            }

            index = end < 0 ? less.Length : end;
        }
        return highest;
    }
}
