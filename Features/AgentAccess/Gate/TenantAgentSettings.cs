using Microsoft.EntityFrameworkCore;

namespace Hub.Features.AgentAccess.Gate;

/// <summary>The agent access columns of the tenant's single tblSetting row.</summary>
public sealed record TenantAgentSettingRow(bool SwitchOn, bool ForcedOff);

// Reads AgentAccessEnabled and AgentAccessForcedOff from tblSetting in one query. Returns null
// when there is no settings row. Throws when the columns do not exist yet (migration not applied), which the gate
// treats as closed.
public interface ITenantAgentSettingSource
{
    Task<TenantAgentSettingRow?> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Raw read, so the scaffolded TblSetting entity (selected by every other query) is untouched.</summary>
public sealed class SqlTenantAgentSettingSource(DynamicDespatchDbContext context) : ITenantAgentSettingSource
{
    private sealed record Row(bool SwitchOn, bool ForcedOff);

    public async Task<TenantAgentSettingRow?> GetAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Database
            .SqlQuery<Row>($"""
                SELECT TOP (1) AgentAccessEnabled AS SwitchOn, AgentAccessForcedOff AS ForcedOff
                FROM dbo.tblSetting ORDER BY SettingId
                """)
            .ToListAsync(cancellationToken);

        return rows.Count == 0 ? null : new TenantAgentSettingRow(rows[0].SwitchOn, rows[0].ForcedOff);
    }
}
