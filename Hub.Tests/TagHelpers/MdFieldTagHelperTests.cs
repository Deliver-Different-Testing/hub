using Hub.TagHelpers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Hub.Tests.TagHelpers;

public class MdFieldTagHelperTests
{
    // The real ValidationHtmlAttributeProvider needs the full MVC options stack;
    // the name/id/value contract under test doesn't depend on it, so stub it out.
    private sealed class NoopValidationProvider : ValidationHtmlAttributeProvider
    {
        public override void AddValidationAttributes(
            ViewContext viewContext,
            ModelExplorer modelExplorer,
            IDictionary<string, string> attributes)
        {
        }
    }

    private static (TagHelperContext, TagHelperOutput) NewContextAndOutput(TagHelperAttributeList? attrs = null)
    {
        var output = new TagHelperOutput(
            "md-outlined-text-field",
            attrs ?? [],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        var context = new TagHelperContext(
            [],
            new Dictionary<object, object>(),
            "test");
        return (context, output);
    }

    private static MdFieldTagHelper NewHelper(string name, object? model)
    {
        var metadataProvider = new EmptyModelMetadataProvider();
        var explorer = metadataProvider.GetModelExplorerForType(typeof(string), model);
        var viewContext = new ViewContext
        {
            ViewData = new ViewDataDictionary(metadataProvider, new ModelStateDictionary()),
        };

        return new MdFieldTagHelper(new NoopValidationProvider())
        {
            For = new ModelExpression(name, explorer),
            ViewContext = viewContext,
        };
    }

    [Fact]
    public void Process_SetsNameIdAndValueFromModelExpression()
    {
        var (context, output) = NewContextAndOutput();
        var helper = NewHelper("Email", "test@example.com");

        helper.Process(context, output);

        Assert.Equal("Email", output.Attributes["name"].Value);
        Assert.Equal("Email", output.Attributes["id"].Value);
        Assert.Equal("test@example.com", output.Attributes["value"].Value);
    }

    [Fact]
    public void Process_DoesNotEmitValue_WhenModelIsEmpty()
    {
        var (context, output) = NewContextAndOutput();
        var helper = NewHelper("Email", null);

        helper.Process(context, output);

        Assert.Equal("Email", output.Attributes["name"].Value);
        Assert.False(output.Attributes.ContainsName("value"));
    }

    [Fact]
    public void Process_DoesNotOverrideExplicitAttributes()
    {
        var attrs = new TagHelperAttributeList
        {
            new TagHelperAttribute("name", "CustomName"),
            new TagHelperAttribute("id", "custom-id"),
        };
        var (context, output) = NewContextAndOutput(attrs);
        var helper = NewHelper("Email", "test@example.com");

        helper.Process(context, output);

        Assert.Equal("CustomName", output.Attributes["name"].Value);
        Assert.Equal("custom-id", output.Attributes["id"].Value);
    }
}
