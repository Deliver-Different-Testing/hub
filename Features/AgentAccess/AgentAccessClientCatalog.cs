using Hub.Interfaces;
using Hub.Services;

namespace Hub.Features.AgentAccess;

/// <summary>Display details for the configured AI apps (AgentAccess:Clients) that are not stored in the OAuth tables.</summary>
public sealed class AgentAccessClientCatalog(IReadOnlyList<AgentAccessClientOptions> clients)
{
    public string? FindVendor(string clientId)
    {
        var vendor = clients.FirstOrDefault(c => string.Equals(c.ClientId, clientId, StringComparison.Ordinal))?.Vendor;
        return string.IsNullOrWhiteSpace(vendor) ? null : vendor;
    }
}
