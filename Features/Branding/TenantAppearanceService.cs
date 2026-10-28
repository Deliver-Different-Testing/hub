using Hub.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Hub.Features.Branding;

public enum TenantAppearanceStatus { TenantNotFound, None, Found }

public sealed record TenantAppearanceLookup(TenantAppearanceStatus Status, TenantAppearance? Appearance)
{
    public static TenantAppearanceLookup NotFound { get; } = new(TenantAppearanceStatus.TenantNotFound, null);
    public static TenantAppearanceLookup NoneConfigured { get; } = new(TenantAppearanceStatus.None, null);
    public static TenantAppearanceLookup Found(TenantAppearance appearance) => new(TenantAppearanceStatus.Found, appearance);
}

public interface ITenantAppearanceService
{
    Task<TenantAppearanceLookup> GetAsync(int tenantId, CancellationToken cancellationToken = default);
}

public interface ITenantAppearanceSource
{
    /// <summary>The raw AppearanceDefaultsJson of the tenant's settings row; null when absent or the column is not migrated yet.</summary>
    Task<string?> ReadJsonAsync(string tenantConnectionString, CancellationToken cancellationToken = default);
}

public sealed class TenantAppearanceService(
    IAuthenticationRepository master,
    ITenantAppearanceSource source,
    IMemoryCache cache) : ITenantAppearanceService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<TenantAppearanceLookup> GetAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var lookup = await cache.GetOrCreateAsync($"tenant_appearance_{tenantId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await LoadAsync(tenantId, cancellationToken);
        });

        return lookup!;
    }

    private async Task<TenantAppearanceLookup> LoadAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connection = await master.GetTenantConnectionStringAsync(tenantId);
        if (string.IsNullOrEmpty(connection)) return TenantAppearanceLookup.NotFound;

        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        var json = await source.ReadJsonAsync(connection + credentials, cancellationToken);
        return TenantAppearanceParser.Parse(json) is { } appearance
            ? TenantAppearanceLookup.Found(appearance)
            : TenantAppearanceLookup.NoneConfigured;
    }
}

/// <summary>Projects only the one column, so tenants where it is missing fail with SQL 207 and are served the built-in appearance.</summary>
public sealed class SqlTenantAppearanceSource(IConnectionStringManager connectionStringManager, DynamicDespatchDbContext context)
    : ITenantAppearanceSource
{
    private const int InvalidColumnName = 207;

    public async Task<string?> ReadJsonAsync(string tenantConnectionString, CancellationToken cancellationToken = default)
    {
        connectionStringManager.SetConnectionString(tenantConnectionString);
        try
        {
            return await context.TblSettings
                .Select(s => s.AppearanceDefaultsJson)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == InvalidColumnName)
        {
            Log.Warning("tblSetting.AppearanceDefaultsJson does not exist on this tenant database yet; serving the built-in appearance");
            return null;
        }
    }
}
