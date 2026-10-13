using Hub.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hub.Services;

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
