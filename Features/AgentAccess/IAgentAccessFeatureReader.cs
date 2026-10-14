using Hub.Interfaces;

namespace Hub.Features.AgentAccess;

public interface IAgentAccessFeatureReader
{
    /// <summary>ReleaseStatus of the agent-access Feature row, or null when the row does not exist.</summary>
    Task<string?> GetReleaseStatusAsync(CancellationToken cancellationToken = default);
}
