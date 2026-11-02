using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hub.Features.Branding;

/// <summary>The validated tenant appearance defaults served to every DFRNT app. Null fields are omitted.</summary>
public sealed record TenantAppearance(
    [property: JsonPropertyName("contrast"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Contrast,
    [property: JsonPropertyName("primary"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Primary,
    [property: JsonPropertyName("secondary"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Secondary,
    [property: JsonPropertyName("textLight"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TextLight,
    [property: JsonPropertyName("textDark"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TextDark);

public static partial class TenantAppearanceParser
{
    private static readonly string[] ContrastValues = ["auto", "standard", "high"];

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();

    /// <summary>Valid fields of the stored JSON, or null when it is empty, malformed or nothing in it is valid.</summary>
    public static TenantAppearance? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            var root = document.RootElement;
            var contrast = Read(root, "contrast") is { } c && ContrastValues.Contains(c) ? c : null;
            var appearance = new TenantAppearance(
                contrast,
                Colour(root, "primary"),
                Colour(root, "secondary"),
                Colour(root, "textLight"),
                Colour(root, "textDark"));

            return appearance == new TenantAppearance(null, null, null, null, null) ? null : appearance;
        }
    }

    private static string? Read(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Colour(JsonElement root, string name) =>
        Read(root, name) is { } value && HexColour().IsMatch(value) ? value.ToLowerInvariant() : null;
}
