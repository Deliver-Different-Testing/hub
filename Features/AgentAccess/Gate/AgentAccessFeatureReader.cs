using Microsoft.EntityFrameworkCore;

namespace Hub.Features.AgentAccess.Gate;

/// <inheritdoc cref="IAgentAccessFeatureReader"/>
public sealed class AgentAccessFeatureReader(DynamicDespatchDbContext context) : IAgentAccessFeatureReader
{
    public async Task<string?> GetReleaseStatusAsync(CancellationToken cancellationToken = default) =>
        await context.Features
            .AsNoTracking()
            .Where(f => f.FeatureKey == AgentAccessGate.FeatureKey)
            .Select(f => f.ReleaseStatus)
            .FirstOrDefaultAsync(cancellationToken);
}

public interface IAgentAccessFeatureReader
{
    /// <summary>ReleaseStatus of the agent-access Feature row, or null when the row does not exist.</summary>
    Task<string?> GetReleaseStatusAsync(CancellationToken cancellationToken = default);
}
