using System.Text.Json.Serialization;

namespace Hub.Features.AiAssistant;

public sealed class AiAssistantStreamEvent
{
    public string Type { get; init; } = string.Empty;
    public string? Text { get; init; }
    public string? ToolName { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<KbSource>? Sources { get; init; }

    /// <summary>Status to answer with when the error arrives before any output; never written into the stream.</summary>
    [JsonIgnore]
    public int? HttpStatus { get; init; }
}
