using Hub.Interfaces;

namespace Hub.Features.AgentAccess;

public interface IAgentAccessKeySource
{
    /// <summary>Reads every parameter under the prefix, keyed by its leaf name (SigningKey, EncryptionKey, ...).</summary>
    Task<IReadOnlyDictionary<string, string>> ReadAsync(string prefix, CancellationToken cancellationToken = default);
}
