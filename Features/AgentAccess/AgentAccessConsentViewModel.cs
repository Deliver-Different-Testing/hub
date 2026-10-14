using Hub.ViewModels;

namespace Hub.Features.AgentAccess;

public sealed record AgentAccessConsentViewModel(
    string ClientName,
    string? Vendor,
    string ResourceHost,
    IReadOnlyList<string> Scopes);
