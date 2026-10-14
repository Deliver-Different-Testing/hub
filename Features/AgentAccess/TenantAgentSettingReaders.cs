using Microsoft.EntityFrameworkCore;
using Hub.Interfaces;
using Hub.Services;

namespace Hub.Features.AgentAccess;

/// <inheritdoc cref="ITenantLifecycleReader"/>
public sealed class TenantLifecycleReader(ITenantAgentSettingSource source) : ITenantLifecycleReader
{
    public async Task<TenantLifecycleState> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await source.GetAsync(cancellationToken);
        var stored = row?.LifecycleState?.Trim();

        // Names only: a number or an unlisted word such as "Beta" is not a state.
        return Enum.GetNames<TenantLifecycleState>().FirstOrDefault(n => string.Equals(n, stored, StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<TenantLifecycleState>(name)
            : TenantLifecycleState.Unknown;
    }
}

/// <inheritdoc cref="IAgentAccessTenantState"/>
public sealed class AgentAccessTenantStateReader(ITenantAgentSettingSource source) : IAgentAccessTenantState
{
    public async Task<AgentAccessTenantState> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await source.GetAsync(cancellationToken);
        return row is null
            ? new AgentAccessTenantState(SwitchOn: false, ForcedOff: false)
            : new AgentAccessTenantState(row.SwitchOn, row.ForcedOff);
    }
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
