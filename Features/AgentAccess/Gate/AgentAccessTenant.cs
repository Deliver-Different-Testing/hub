using Hub.Interfaces;
using Hub.Services;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Hub.Features.AgentAccess.Gate;

/// <summary>The tenant this Hub serves for agent access.</summary>
public sealed record AgentAccessTenant(int TenantId, string Code, string Name);

/// <summary>This deployment's agent access issuer (env AgentAccessIssuer); null when it is unset or invalid.</summary>
public sealed record AgentAccessIssuer(Uri? Value);

// Which tenant this Hub serves for agent access, worked out with no one signed in: an AI app's first calls (discovery,
// authorize, token) carry no Hub cookie. The tenant comes from the configured issuer's host, never the request's Host
// header, so it is fixed per deployment and always matches the tokens' iss; the shared staging host serves the stack
// tenant (AccountService.ExtractTenantFromHost). Resolving also points this request's tenant database at that tenant,
// so the agent access gate can read its settings. Null when the tenant cannot be worked out.
public interface IAgentAccessTenantResolver
{
    Task<AgentAccessTenant?> ResolveAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAgentAccessTenantResolver"/>
public sealed class AgentAccessTenantResolver(
    AgentAccessIssuer issuer,
    IAuthenticationRepository master,
    IConnectionStringManager connectionStringManager,
    IMemoryCache cache,
    Func<string?>? sqlCredentials = null) : IAgentAccessTenantResolver
{
    private static readonly TimeSpan FoundFor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MissingFor = TimeSpan.FromSeconds(30);

    // Carries the connection string with its credentials, so it stays in this process's memory and never in Redis.
    private sealed record Entry(AgentAccessTenant? Tenant, string? Connection);

    public async Task<AgentAccessTenant?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var code = issuer.Value is { } uri ? AccountService.ExtractTenantFromHost(uri.Host) : null;
        if (code is null)
        {
            Log.Warning("Agent access cannot tell which tenant this Hub serves from its issuer {Issuer}", issuer.Value);
            return null;
        }

        // A failed Master DB read throws out of the factory, so it is not cached.
        var entry = await cache.GetOrCreateAsync($"agent_access_tenant_{code.ToLowerInvariant()}", async item =>
        {
            var loaded = await LoadAsync(code);
            item.AbsoluteExpirationRelativeToNow = loaded.Tenant is null ? MissingFor : FoundFor;
            return loaded;
        });

        if (entry?.Tenant is null || string.IsNullOrEmpty(entry.Connection)) return null;

        connectionStringManager.SetConnectionString(entry.Connection);
        return entry.Tenant;
    }

    private async Task<Entry> LoadAsync(string code)
    {
        var tenant = await master.GetTenantByCodeAsync(code);
        var credentials = (sqlCredentials ?? (() => Environment.GetEnvironmentVariable("SQLCredentials")))();
        if (tenant is null || string.IsNullOrEmpty(tenant.Dbconnection) || string.IsNullOrEmpty(credentials))
        {
            Log.Warning("Agent access cannot find tenant {TenantCode} or its database", code);
            return new Entry(null, null);
        }

        var name = string.IsNullOrWhiteSpace(tenant.Name) ? tenant.Code ?? code : tenant.Name;
        return new Entry(new AgentAccessTenant(tenant.TenantId, tenant.Code ?? code, name), tenant.Dbconnection + credentials);
    }
}
