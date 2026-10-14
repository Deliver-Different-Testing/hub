namespace Hub.Services;

/// <param name="ConnectionString">The connection string the OAuth store will use.</param>
/// <param name="Dedicated">True when it is the least-privilege McpOAuthStore login, false when it fell back to MasterSQLConnection.</param>
/// <param name="Description">A line safe to log: says which one is in use and never contains a connection string.</param>
public sealed record AgentAccessOAuthStoreConnection(string ConnectionString, bool Dedicated, string Description);

/// <summary>
/// Chooses the connection string for the OAuth store. The dedicated login can reach only the three
/// McpOAuth tables, so a flaw in the OAuth store cannot touch tenants, users or connection strings in
/// the Master database. Falling back to MasterSQLConnection keeps Hub working until the login exists.
/// </summary>
public static class AgentAccessOAuthStore
{
    public static AgentAccessOAuthStoreConnection ResolveConnectionString(
        string masterConnectionString, string? dedicatedConnectionString)
    {
        return string.IsNullOrWhiteSpace(dedicatedConnectionString)
            ? new AgentAccessOAuthStoreConnection(
                masterConnectionString,
                Dedicated: false,
                "AgentAccessOAuthSQLConnection is not set; the OAuth store uses MasterSQLConnection, which can reach far more than it needs")
            : new AgentAccessOAuthStoreConnection(
                dedicatedConnectionString,
                Dedicated: true,
                "the OAuth store uses its dedicated least-privilege login");
    }
}
