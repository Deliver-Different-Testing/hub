using Hub.Interfaces;

namespace Hub.Features.AgentAccess;

// Whether the OAuth server runs on persistent keys from SSM and a configured issuer
// (phase 1.3). Decided once at startup. When it is not persistent Hub still starts, on
// per-pod ephemeral keys, but the agent access gate stays closed: tokens signed with a
// key another pod cannot verify must never be issued.
public interface IAgentAccessKeyState
{
    bool Persistent { get; }

    /// <summary>Why the keys are, or are not, persistent. Never contains key material.</summary>
    string Reason { get; }

    /// <summary>Key id of the current signing key when persistent; safe to log.</summary>
    string? SigningKeyId { get; }
}
