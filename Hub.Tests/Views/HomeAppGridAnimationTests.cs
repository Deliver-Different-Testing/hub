using Hub.Services;

namespace Hub.Tests.Views;

public class HomeAppGridAnimationTests
{
    private readonly string _siteLess;

    public HomeAppGridAnimationTests()
    {
        // The view is no longer read: the tile list moved to HubTileCatalogue on
        // 2026-09-01, so the count comes from there rather than from markup.
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
    public void Animation_Covers_EveryTileTheCatalogueCanRender()
    {
        // This used to count cards in the internal-staff block of Index.cshtml.
        // That block is gone as of 2026-09-01 — the tiles come from
        // HubTileCatalogue and the matrix decides which of them render — so the
        // question is asked of the catalogue directly, which is also the thing
        // that grows when someone adds a tile.
        //
        // site.less staggers the entry animation per :nth-child. A tile added to
        // the catalogue without extending that rule pops in alongside the first
        // card instead of trailing the rest.
        var tileCount = HubTileCatalogue.Keys.Count;
        var highestDelayedChild = HighestNthChildDelayInAppGrid(_siteLess);

        Assert.True(
            highestDelayedChild >= tileCount,
            $"site.less staggers up to :nth-child({highestDelayedChild}) but the catalogue can "
            + $"render {tileCount} tiles. Extend the nth-child rules in site.less.");
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
