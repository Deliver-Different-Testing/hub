namespace Hub.Features.AiAssistant;

public sealed class AiAssistantOptions
{
    public const string SectionName = "AiAssistant";

    /// <summary>The Anthropic key is shared config, not part of the AiAssistant section.</summary>
    public const string ApiKeyPath = "AutoMate:Anthropic:ApiKey";

    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-haiku-5-5";
    public string? Effort { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxRetries { get; set; } = 2;
    public int MaxTokens { get; set; } = 4096;
    public int MaxConversationMessages { get; set; } = 50;
    public int MaxToolRounds { get; set; } = 3;
    public int MaxToolCallsPerTurn { get; set; } = 10;
    public int MaxToolResultChars { get; set; } = 12000;
    public int MaxMessageChars { get; set; } = 8000;
    public int MaxPageContextChars { get; set; } = 200;
    public int GroundingTimeoutMs { get; set; } = 2000;
    public int KbToolTimeoutMs { get; set; } = 5000;

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}
