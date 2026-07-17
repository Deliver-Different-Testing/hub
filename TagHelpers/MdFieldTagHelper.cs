using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Hub.TagHelpers;

/// <summary>
/// Lets <c>asp-for</c> work on Material Design 3 (@material/web) form fields.
/// The built-in Input/Select tag helpers only target &lt;input&gt;/&lt;select&gt;/&lt;textarea&gt;,
/// so on a custom element they emit nothing. This copies the model-derived
/// <c>name</c>, <c>id</c>, <c>value</c> (so FormData / model binding work — the
/// components are form-associated) plus the unobtrusive <c>data-val-*</c>
/// validation attributes onto the element.
/// </summary>
[HtmlTargetElement("md-outlined-text-field", Attributes = ForAttributeName)]
[HtmlTargetElement("md-filled-text-field", Attributes = ForAttributeName)]
[HtmlTargetElement("md-outlined-select", Attributes = ForAttributeName)]
[HtmlTargetElement("md-filled-select", Attributes = ForAttributeName)]
public class MdFieldTagHelper(ValidationHtmlAttributeProvider validationAttributeProvider) : TagHelper
{
    private const string ForAttributeName = "asp-for";

    [HtmlAttributeName(ForAttributeName)]
    public ModelExpression For { get; set; } = null!;

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var fullName = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);

        if (!string.IsNullOrEmpty(fullName))
        {
            if (!output.Attributes.ContainsName("name"))
            {
                output.Attributes.SetAttribute("name", fullName);
            }

            if (!output.Attributes.ContainsName("id"))
            {
                output.Attributes.SetAttribute("id", TagBuilder.CreateSanitizedId(fullName, "_"));
            }
        }

        if (!output.Attributes.ContainsName("value"))
        {
            var value = For.Model?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                output.Attributes.SetAttribute("value", value);
            }
        }

        // Unobtrusive validation attributes — harmless where unused, available
        // for forms that wire jQuery validation. The auth forms validate in
        // their own TS instead and simply ignore these.
        var validationAttributes = new Dictionary<string, string>(StringComparer.Ordinal);
        validationAttributeProvider.AddValidationAttributes(ViewContext, For.ModelExplorer, validationAttributes);
        foreach (var attribute in validationAttributes.Where(attribute => !output.Attributes.ContainsName(attribute.Key)))
        {
            output.Attributes.SetAttribute(attribute.Key, attribute.Value);
        }
    }
}
