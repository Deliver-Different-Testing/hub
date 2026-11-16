using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hub.Features.AgentAccess.Keys;

/// <summary>
/// Degraded while the keys are still being retried, so it shows on /healthz without failing readiness: Hub serves login
/// without them, and a dependency must never take the pod out of service. Reads cached state only, never SSM.
/// </summary>
public sealed class AgentAccessKeyHealthCheck(IAgentAccessKeyState keys) : IHealthCheck
{
    public const string Name = "agent-access-keys";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            keys.Persistent ? HealthCheckResult.Healthy("agent access keys are persistent")
            : keys.Transient ? HealthCheckResult.Degraded($"agent access keys are still being retried: {keys.Reason}")
            : HealthCheckResult.Healthy($"agent access is closed by configuration: {keys.Reason}"));
}
