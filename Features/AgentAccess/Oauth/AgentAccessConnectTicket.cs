using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Hub.Features.AgentAccess.Oauth;

/// <summary>One parameter of the original authorization request, posted back by the SPA's form as a hidden field.</summary>
public sealed record AgentAccessParameter(string Name, string Value);

/// <summary>
/// What the SPA's connect page needs, sealed by the authorization endpoint once OpenIddict has validated the request.
/// The page never reads OAuth parameters from its own URL: it shows what this ticket says and posts the parameters
/// back to /connect/authorize, where OpenIddict validates them again.
/// </summary>
public sealed record AgentAccessConnectTicket(
    string Kind,
    string UserId,
    int TenantId,
    IReadOnlyList<AgentAccessParameter> Parameters,
    string ClientName,
    string? VerifiedVendor,
    string? ClientHost,
    string ResourceHost,
    string? RedirectHost,
    string? CurrentOrganisation = null,
    string? HubOrganisation = null,
    bool CanSwitch = false,
    string? Error = null)
{
    public const string Consent = "consent";
    public const string Switch = "switch";
}

// Data protection is shared across Hub's pods (and the other DeliverDifferent apps), so a ticket minted by one pod
// opens on another. Ten minutes is ample to read a consent page; after that the person connects again from the app.
public sealed class AgentAccessConnectTickets(IDataProtectionProvider provider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector protector =
        provider.CreateProtector("Hub.AgentAccess.ConnectTicket").ToTimeLimitedDataProtector();

    public string Protect(AgentAccessConnectTicket ticket) =>
        protector.Protect(JsonSerializer.Serialize(ticket), Lifetime);

    /// <summary>The ticket, or null when it is missing, tampered with or expired.</summary>
    public AgentAccessConnectTicket? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return JsonSerializer.Deserialize<AgentAccessConnectTicket>(protector.Unprotect(value));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}
