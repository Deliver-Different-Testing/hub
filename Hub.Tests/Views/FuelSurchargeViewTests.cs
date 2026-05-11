using Hub.ViewModels;

namespace Hub.Tests.Views;

public class FuelSurchargeViewTests
{
    private readonly string _viewContent;
    private readonly string _siteCss;

    public FuelSurchargeViewTests()
    {
        var viewPath = FindFile("Views", "FuelSurcharge", "Index.cshtml");
        if (viewPath is null)
            Assert.Skip("Views/FuelSurcharge/Index.cshtml not found relative to test output directory");
        _viewContent = File.ReadAllText(viewPath);

        var cssPath = FindFile("wwwroot", "css", "site.less");
        _siteCss = cssPath is null ? string.Empty : File.ReadAllText(cssPath);
    }

    private static string ExtractRule(string css, string selector)
    {
        var marker = selector + " {";
        var start = css.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var depth = 0;
        for (var i = start + marker.Length - 1; i < css.Length; i++)
        {
            switch (css[i])
            {
                case '{':
                    depth++;
                    break;
                case '}' when --depth == 0:
                    return css.Substring(start, i - start + 1);
            }
        }
        return string.Empty;
    }

    private static string? FindFile(params string[] pathSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. pathSegments]);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void View_DoesNotRender_ClientSpecificStatCard()
    {
        // The "Client-specific" stat card was removed so the page focuses on
        // standard surcharge and pump price data only.
        Assert.DoesNotContain("Client-specific</span>", _viewContent);
        Assert.DoesNotContain("CurrentClientSpecific", _viewContent);
        Assert.DoesNotContain("No client override active", _viewContent);
    }

    [Fact]
    public void View_DoesNotRender_ClientColumnInHistoryTable()
    {
        // The "Client" column was removed from the Recent records table; the
        // Scope chip still distinguishes standard vs client-specific rows.
        Assert.DoesNotContain("<th scope=\"col\">Client</th>", _viewContent);
        Assert.DoesNotContain("row.ClientName", _viewContent);
    }

    [Fact]
    public void View_StillRenders_SurvivingStatCards()
    {
        Assert.Contains("Current standard", _viewContent);
        Assert.Contains("Pump price", _viewContent);
        Assert.Contains("Visible records", _viewContent);
    }

    [Fact]
    public void View_StillRenders_SurvivingHistoryColumns()
    {
        Assert.Contains(">Status</th>", _viewContent);
        Assert.Contains(">Scope</th>", _viewContent);
        Assert.Contains(">Rate</th>", _viewContent);
        Assert.Contains(">Pump price</th>", _viewContent);
        Assert.Contains(">Start</th>", _viewContent);
        Assert.Contains(">End</th>", _viewContent);
    }

    [Fact]
    public void FormatPercent_ScalesFractionToPercent_WithOneDecimal()
    {
        // Rates are stored in the DB as fractions (e.g. 0.132 = 13.2%). The
        // formatter must multiply by 100 and use one decimal place so 0.132
        // renders as "13.2%", not ".13%".
        Assert.Contains("(value.Value * 100m).ToString(\"0.0\", nz) + \"%\"", _viewContent);
        Assert.DoesNotContain("value.Value.ToString(\"0.00\", nz) + \"%\"", _viewContent);
    }

    [Fact]
    public void HistoryTable_AllColumns_AreSortable()
    {
        // Every column header carries a data-fuel-sort key and an initial
        // aria-sort="none" so the JS click handler and the CSS chevron both
        // hook up. Losing either attribute breaks sort affordance silently.
        string[] keys = ["status", "scope", "rate", "pump", "start", "end"];
        foreach (var key in keys)
            Assert.Contains($"data-fuel-sort=\"{key}\"", _viewContent);
        Assert.Equal(keys.Length, CountOccurrences(_viewContent, "aria-sort=\"none\""));
    }

    [Fact]
    public void HistoryTable_Cells_CarrySortValues_ForNonTextColumns()
    {
        // Rate/pump cells need raw decimal values, dates need ISO yyyy-MM-dd,
        // and status needs a priority key — otherwise sorting falls back to
        // localised display text and orders e.g. "5.00%" before "10.00%".
        Assert.Contains("data-sort-value=\"@statusSort\"", _viewContent);
        Assert.Contains("data-sort-value=\"@row.ScopeLabel\"", _viewContent);
        Assert.Contains("data-sort-value=\"@DecimalSort(row.Rate)\"", _viewContent);
        Assert.Contains("data-sort-value=\"@DecimalSort(row.PumpPrice)\"", _viewContent);
        Assert.Contains("data-sort-value=\"@IsoDate(row.Start)\"", _viewContent);
        Assert.Contains("data-sort-value=\"@IsoDate(row.End)\"", _viewContent);
    }

    [Fact]
    public void HistoryTable_HasHorizontalCellPadding()
    {
        // Bootstrap's default .5rem horizontal cell padding left chevrons
        // hugging the next column. Both th and td should carry the larger
        // padding, and the chevron should reserve a fixed width so the gap
        // doesn't change between ↕/↑/↓ glyphs.
        if (string.IsNullOrEmpty(_siteCss))
            Assert.Skip("wwwroot/css/site.less not found relative to test output directory");

        var tableRule = ExtractRule(_siteCss, ".fuel-history-table");
        Assert.Equal(2, CountOccurrences(tableRule, "padding: .65rem 1rem;"));
        Assert.Contains("width: .75em;", tableRule);
    }

    [Fact]
    public void SortableHeaders_HaveChevronStyles()
    {
        if (string.IsNullOrEmpty(_siteCss))
            Assert.Skip("wwwroot/css/site.less not found relative to test output directory");

        Assert.Contains("th[data-fuel-sort]", _siteCss);
        Assert.Contains("th[aria-sort='ascending']", _siteCss);
        Assert.Contains("th[aria-sort='descending']", _siteCss);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    [Fact]
    public void TopStatCards_Are_CenteredAndMatchCardCount()
    {
        // After dropping the Client-specific card the grid was left at 4 columns,
        // anchoring the remaining 3 cards to the left. The layout should match the
        // actual card count and center each card's text so the row reads balanced.
        if (string.IsNullOrEmpty(_siteCss))
            Assert.Skip("wwwroot/css/site.less not found relative to test output directory");

        var statsRule = ExtractRule(_siteCss, ".fuel-surcharge-stats");
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr));", statsRule);
        Assert.DoesNotContain("repeat(4, minmax(0, 1fr))", _siteCss);

        var statRule = ExtractRule(_siteCss, ".fuel-stat");
        Assert.Contains("align-items: center;", statRule);
        Assert.Contains("text-align: center;", statRule);
    }

    [Fact]
    public void ViewModel_DoesNotExpose_CurrentClientSpecific()
    {
        // The view no longer needs this property; removing it from the VM
        // prevents the column/card sneaking back in without an explicit change.
        var property = typeof(FuelSurchargeCardViewModel).GetProperty("CurrentClientSpecific");
        Assert.Null(property);
    }
}
