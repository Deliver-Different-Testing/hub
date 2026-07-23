using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Hub.TagHelpers;

/// <summary>
/// Inlines a DFRNT UI icon as an SVG. The brand book ("Iconography - UI") pairs
/// two libraries — Lucide for generic UI chrome, Tabler for transport/logistics
/// — and mandates a single wrapper that bakes in size and stroke so no call site
/// uses the libraries' (mismatched) bare defaults.
///
/// This is that wrapper for the ASP.NET/Razor stack (the book's sample is React):
/// it re-emits every icon at the confirmed <b>stroke width 1.25</b> and a
/// caller-chosen <b>size</b> (default 24px for dense UI chrome; pass <c>size</c>
/// for larger uses), with <c>stroke="currentColor"</c> so icons inherit the
/// active <c>--dd-*</c> theme colour.
///
/// Usage: <c>&lt;dfrnt-icon set="lucide" name="settings" /&gt;</c>, or inside an
/// MD3 slot: <c>&lt;md-icon slot="start"&gt;&lt;dfrnt-icon name="log-out" /&gt;&lt;/md-icon&gt;</c>.
///
/// Source SVGs are synced into <c>wwwroot/dist/icons/{set}/{name}.svg</c> by the
/// esbuild build step (see esbuild.config.ts) — node_modules is not deployed,
/// wwwroot/dist is.
/// </summary>
[HtmlTargetElement("dfrnt-icon", TagStructure = TagStructure.WithoutEndTag)]
public partial class DfrntIconTagHelper(IWebHostEnvironment env) : TagHelper
{
    /// <summary>Confirmed brand spec — same stroke for both libraries.</summary>
    public const string StrokeWidth = "1.25";

    /// <summary>Default size for dense UI chrome. Override via <c>size</c>.</summary>
    public const int DefaultSize = 24;

    private static readonly ConcurrentDictionary<string, ParsedIcon?> Cache = new(StringComparer.Ordinal);
    private static readonly Regex ViewBoxRegex = MyRegex();
    private static readonly Regex SafeToken = MyRegex1();

    private sealed record ParsedIcon(string ViewBox, string Inner);

    /// <summary>Icon library: "lucide" (UI chrome) or "tabler" (logistics).</summary>
    public string Set { get; set; } = "lucide";

    /// <summary>Icon file name, e.g. "settings", "log-out", "chevron-down".</summary>
    public string Name { get; set; } = "";

    /// <summary>Rendered width/height in px. Stroke stays 1.25 regardless.</summary>
    public int Size { get; set; } = DefaultSize;

    /// <summary>
    /// Accessible label. When set the icon is exposed to assistive tech
    /// (role="img"); when omitted the icon is decorative (aria-hidden).
    /// </summary>
    public string? Label { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var icon = Resolve(Set, Name);
        if (icon is null)
        {
            // Loud-but-safe: emit a comment instead of breaking the page. The
            // build step warns for the same miss, so typos surface in CI too.
            output.TagName = null;
            output.Content.SetHtmlContent(new HtmlString($"<!-- dfrnt-icon: {Set}/{Name} not found -->"));
            return;
        }

        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;

        var existingClass = output.Attributes["class"]?.Value?.ToString();
        var cssClass = string.IsNullOrEmpty(existingClass) ? "dfrnt-icon" : $"dfrnt-icon {existingClass}";
        output.Attributes.SetAttribute("class", cssClass);

        output.Attributes.SetAttribute("xmlns", "http://www.w3.org/2000/svg");
        output.Attributes.SetAttribute("width", Size);
        output.Attributes.SetAttribute("height", Size);
        output.Attributes.SetAttribute("viewBox", icon.ViewBox);
        output.Attributes.SetAttribute("fill", "none");
        output.Attributes.SetAttribute("stroke", "currentColor");
        output.Attributes.SetAttribute("stroke-width", StrokeWidth);
        output.Attributes.SetAttribute("stroke-linecap", "round");
        output.Attributes.SetAttribute("stroke-linejoin", "round");

        if (string.IsNullOrWhiteSpace(Label))
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
            output.Attributes.SetAttribute("focusable", "false");
        }
        else
        {
            output.Attributes.SetAttribute("role", "img");
            output.Attributes.SetAttribute("aria-label", Label);
        }

        output.Content.SetHtmlContent(new HtmlString(icon.Inner));
    }

    private ParsedIcon? Resolve(string set, string name)
    {
        if (!SafeToken.IsMatch(set) || !SafeToken.IsMatch(name))
        {
            return null;
        }

        return Cache.GetOrAdd($"{set}/{name}", _ => Load(set, name));
    }

    private ParsedIcon? Load(string set, string name)
    {
        var file = Path.Combine(env.WebRootPath, "dist", "icons", set, name + ".svg");
        if (!File.Exists(file))
        {
            return null;
        }

        var svg = File.ReadAllText(file);

        var open = svg.IndexOf("<svg", StringComparison.Ordinal);
        var openEnd = open < 0 ? -1 : svg.IndexOf('>', open);
        var close = svg.LastIndexOf("</svg>", StringComparison.Ordinal);
        if (openEnd < 0 || close < 0 || close < openEnd)
        {
            return null;
        }

        var openTag = svg[open..openEnd];
        var viewBox = ViewBoxRegex.Match(openTag) is { Success: true } m ? m.Groups[1].Value : "0 0 24 24";
        var inner = svg[(openEnd + 1)..close].Trim();

        return new ParsedIcon(viewBox, inner);
    }

    [GeneratedRegex("""viewBox="([^"]*)""", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.Compiled)]
    private static partial Regex MyRegex1();
}
