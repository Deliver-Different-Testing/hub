using System.Text.Json;

namespace Hub.Features.AiAssistant.Tools;

public static class ToolSchema
{
    public static JsonElement Prop(string type, string description) =>
        JsonSerializer.SerializeToElement(new { type, description });
}

public static class ToolOutput
{
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Error(string message) => JsonSerializer.Serialize(new { error = message }, Relaxed);

    public static bool IsError(string result) => result.StartsWith("{\"error\":", StringComparison.Ordinal);

    public static string? Truncate(string? value, int maxChars)
    {
        if (value is null || value.Length <= maxChars)
        {
            return value;
        }

        return $"{value[..maxChars]}…[truncated {value.Length - maxChars} chars]";
    }
}

/// <summary>
/// Reads tool arguments without throwing on model-supplied bad input; the first
/// problem is recorded in <see cref="Error"/> so the tool can return it to the model.
/// </summary>
public sealed class ToolArgs(JsonElement input)
{
    public string? Error { get; private set; }

    public string? String(string name)
    {
        if (input.ValueKind != JsonValueKind.Object
            || !input.TryGetProperty(name, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        Error ??= $"Argument '{name}' must be a string.";
        return null;
    }
}
