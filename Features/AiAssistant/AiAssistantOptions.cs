namespace Hub.Features.AiAssistant;

public sealed class AiAssistantOptions
{
    public const string SectionName = "AiAssistant";

    /// <summary>The Anthropic federation is shared config, not part of the AiAssistant section.</summary>
    public const string FederationPath = "AutoMate:Anthropic:Federation";

    public const string NotConnectedMessage = "Sorry, I'm not connected right now. Please try again later.";

    public FederationOptions Federation { get; set; } = new();
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

    public bool Enabled => Federation.IsConfigured && File.Exists(Federation.IdentityTokenFile);

    public sealed class FederationOptions
    {
        public string? RuleId { get; set; }
        public string? OrganizationId { get; set; }
        public string? ServiceAccountId { get; set; }
        public string? WorkspaceId { get; set; }
        public string IdentityTokenFile { get; set; } = "/var/run/secrets/anthropic.com/token";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(RuleId)
            && !string.IsNullOrWhiteSpace(OrganizationId)
            && !string.IsNullOrWhiteSpace(ServiceAccountId);
    }
}
