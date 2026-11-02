namespace Hub.Features.AiAssistant;

public sealed class AiChatRequest
{
    public List<AiChatMessage> Messages { get; init; } = [];
    public string? PageContext { get; init; }
}

public sealed class AiChatMessage
{
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}
