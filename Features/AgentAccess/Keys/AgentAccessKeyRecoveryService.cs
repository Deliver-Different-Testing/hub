using Serilog;

namespace Hub.Features.AgentAccess.Keys;

/// <summary>
/// Runs only when startup could not read the keys from SSM for a passing reason. Keeps trying, with a capped
/// jittered backoff, until they load, then hands them to the <see cref="AgentAccessKeyHolder"/> and stops. The pod
/// keeps serving login meanwhile; only agent access waits.
/// </summary>
public sealed class AgentAccessKeyRecoveryService(AgentAccessKeyHolder holder, AgentAccessOAuthSettings settings) : BackgroundService
{
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseDelay = settings.RecoveryDelay ?? DefaultDelay;
        var maxDelay = settings.RecoveryMaxDelay ?? DefaultMaxDelay;

        for (var attempt = 1; ; attempt++)
        {
            var pause = AgentAccessKeyBootstrap.Backoff(baseDelay, attempt, maxDelay);
            await Task.Delay(pause < baseDelay ? baseDelay : pause, stoppingToken);

            var result = await AgentAccessKeyBootstrap.ResolveAsync(settings, attempts: 1, stoppingToken);
            if (holder.TryRecover(result))
            {
                Log.Information(
                    "Agent access keys recovered after {Attempts} background attempts (signing kid {SigningKeyId}, encryption kid {EncryptionKeyId})",
                    attempt, result.State.SigningKeyId, result.State.EncryptionKeyId);
                return;
            }

            if (holder.Persistent) return;

            if (!result.State.Transient)
            {
                Log.Warning("Agent access keys can no longer be recovered, so the agent access gate stays closed: {Reason}", result.State.Reason);
                return;
            }

            Log.Warning("Agent access keys: background attempt {Attempt} failed: {Reason}", attempt, result.State.Reason);
        }
    }
}
