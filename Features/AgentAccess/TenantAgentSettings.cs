using Microsoft.EntityFrameworkCore;
using Hub.Services;

namespace Hub.Features.AgentAccess;

/// <summary>The agent access columns of the tenant's single tblSetting row.</summary>
public sealed record TenantAgentSettingRow(string? LifecycleState, bool SwitchOn, bool ForcedOff);

// Reads TenantLifecycleState, AgentAccessEnabled and AgentAccessForcedOff from tblSetting in one query. Returns null
// when there is no settings row. Throws when the columns do not exist yet (migration not applied), which the gate
// treats as closed.
public interface ITenantAgentSettingSource
{
    Task<TenantAgentSettingRow?> GetAsync(CancellationToken cancellationToken = default);
}

// Tenant lifecycle states from the environment and release policy, section 4.
public enum TenantLifecycleState
{
    Unknown,
    Provisioned,
    Configuring,
    PreLiveUat,
    ReadyForGoLive,
    Live,
    Suspended,
    Demo
}

public static class TenantLifecycle
{
    // Names only: a number or an unlisted word such as "Beta" is not a state.
    public static TenantLifecycleState Parse(string? stored)
    {
        var trimmed = stored?.Trim();
        return Enum.GetNames<TenantLifecycleState>().FirstOrDefault(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<TenantLifecycleState>(name)
            : TenantLifecycleState.Unknown;
    }

    public static bool AllowsAgentAccess(string? stored) => Parse(stored) is TenantLifecycleState.Live or TenantLifecycleState.Demo;
}

/// <summary>Raw read, so the scaffolded TblSetting entity (selected by every other query) is untouched.</summary>
public sealed class SqlTenantAgentSettingSource(DynamicDespatchDbContext context) : ITenantAgentSettingSource
{
    private sealed record Row(string? LifecycleState, bool SwitchOn, bool ForcedOff);

    public async Task<TenantAgentSettingRow?> GetAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Database
            .SqlQuery<Row>($"""
                SELECT TOP (1) TenantLifecycleState AS LifecycleState, AgentAccessEnabled AS SwitchOn, AgentAccessForcedOff AS ForcedOff
                FROM dbo.tblSetting ORDER BY SettingId
                """)
            .ToListAsync(cancellationToken);

        return rows.Count == 0 ? null : new TenantAgentSettingRow(rows[0].LifecycleState, rows[0].SwitchOn, rows[0].ForcedOff);
    }
}
