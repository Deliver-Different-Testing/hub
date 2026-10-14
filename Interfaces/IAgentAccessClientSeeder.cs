namespace Hub.Interfaces;

// Creates or updates the pre-registered OAuth clients from configuration (phase 1.4). Called by the
// endpoint gate only after the agent access gate has opened for a tenant, so Hub never writes to the
// OAuth store while agent access is dark. Never throws (except for cancellation): a problem is logged
// and retried later, and the OAuth endpoints simply reject unknown clients until it succeeds.
public interface IAgentAccessClientSeeder
{
    Task EnsureAsync(CancellationToken cancellationToken = default);
}
