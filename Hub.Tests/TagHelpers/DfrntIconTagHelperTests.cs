using Hub.TagHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using NSubstitute;

namespace Hub.Tests.TagHelpers;

// Exercises the <dfrnt-icon> TagHelper against the real SVGs synced into
// wwwroot/dist/icons by the build, so it doubles as a render check: the emitted
// markup is the actual DFRNT icon spec — stroke 1.25, currentColor, chosen size.
public class DfrntIconTagHelperTests
{
    private static string WebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "wwwroot");
            if (File.Exists(Path.Combine(candidate, "dist", "icons", "lucide", "settings.svg")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        Assert.Skip("wwwroot/dist/icons not found — run the esbuild build first");
        return "";
    }

    private static DfrntIconTagHelper NewHelper()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.WebRootPath.Returns(WebRoot());
        return new DfrntIconTagHelper(env);
    }

    private static (TagHelperContext, TagHelperOutput) NewContextAndOutput(TagHelperAttributeList? attrs = null)
    {
        var output = new TagHelperOutput(
            "dfrnt-icon",
            attrs ?? [],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        return (context, output);
    }

    [Fact]
    public void SpecConstants_MatchBrandBook()
    {
        Assert.Equal("1.25", DfrntIconTagHelper.StrokeWidth);
        Assert.Equal(24, DfrntIconTagHelper.DefaultSize);
    }

    [Fact]
    public void Process_EmitsSvgWithSpecStrokeAndDefaultSize()
    {
        var (context, output) = NewContextAndOutput();
        var helper = NewHelper();
        helper.Set = "lucide";
        helper.Name = "settings";

        helper.Process(context, output);

        Assert.Equal("svg", output.TagName);
        Assert.Equal("1.25", output.Attributes["stroke-width"].Value);
        Assert.Equal("currentColor", output.Attributes["stroke"].Value);
        Assert.Equal("none", output.Attributes["fill"].Value);
        Assert.Equal(24, output.Attributes["width"].Value);
        Assert.Equal(24, output.Attributes["height"].Value);
        Assert.NotNull(output.Attributes["viewBox"].Value);
        Assert.Contains("dfrnt-icon", output.Attributes["class"].Value!.ToString());
        // Decorative by default.
        Assert.Equal("true", output.Attributes["aria-hidden"].Value);
        // Actual icon geometry made it through (settings.svg has a <circle>).
        Assert.Contains("<circle", output.Content.GetContent());
    }

    [Fact]
    public void Process_RespectsSizeOverride_KeepingStroke()
    {
        var (context, output) = NewContextAndOutput();
        var helper = NewHelper();
        helper.Name = "chevron-left";
        helper.Size = 18;

        helper.Process(context, output);

        Assert.Equal(18, output.Attributes["width"].Value);
        Assert.Equal(18, output.Attributes["height"].Value);
        Assert.Equal("1.25", output.Attributes["stroke-width"].Value);
    }

    [Fact]
    public void Process_WithLabel_ExposesRoleImg_NotAriaHidden()
    {
        var (context, output) = NewContextAndOutput();
        var helper = NewHelper();
        helper.Name = "settings";
        helper.Label = "Settings";

        helper.Process(context, output);

        Assert.Equal("img", output.Attributes["role"].Value);
        Assert.Equal("Settings", output.Attributes["aria-label"].Value);
        Assert.False(output.Attributes.ContainsName("aria-hidden"));
    }

    [Fact]
    public void Process_MergesExistingClass()
    {
        var attrs = new TagHelperAttributeList { new TagHelperAttribute("class", "dropdown-arrow") };
        var (context, output) = NewContextAndOutput(attrs);
        var helper = NewHelper();
        helper.Name = "chevron-down";

        helper.Process(context, output);

        var cssClass = output.Attributes["class"].Value!.ToString()!;
        Assert.Contains("dfrnt-icon", cssClass);
        Assert.Contains("dropdown-arrow", cssClass);
    }

    [Fact]
    public void Process_MissingIcon_RendersCommentNotSvg()
    {
        var (context, output) = NewContextAndOutput();
        var helper = NewHelper();
        helper.Name = "this-icon-does-not-exist";

        helper.Process(context, output);

        Assert.Null(output.TagName);
        Assert.Contains("not found", output.Content.GetContent());
    }

    [Fact]
    public void Process_RejectsUnsafeName()
    {
        var (context, output) = NewContextAndOutput();
        var helper = NewHelper();
        helper.Name = "../../appsettings";

        helper.Process(context, output);

        // Path-traversal names never reach the filesystem — treated as missing.
        Assert.Null(output.TagName);
    }
}
