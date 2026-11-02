namespace Hub.Features.AiAssistant;

public sealed class ServiceAuthOptions
{
    public const string SectionName = "ServiceAuth";

    public string ClientId { get; set; } = "hub";
    public string AssertionPath { get; set; } = "/var/run/secrets/dfrnt/csp-token";
    public string? ClientSecret { get; set; }
}
