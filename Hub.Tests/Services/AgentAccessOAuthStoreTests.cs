using Hub.Services;

namespace Hub.Tests.Services;

// The OAuth store should use its own least-privilege SQL login (McpOAuthStore, which can only reach
// the three McpOAuth tables) through the env var AgentAccessOAuthSQLConnection. Until that variable is
// set, Hub keeps using MasterSQLConnection so nothing breaks, and says so at startup.
public class AgentAccessOAuthStoreTests
{
    private const string Master = "Server=db;Database=Master;User Id=hub;Password=master-secret;";
    private const string Dedicated = "Server=db;Database=Master;User Id=McpOAuthStore;Password=store-secret;";

    [Fact]
    public void A_dedicated_connection_string_is_used_when_set()
    {
        var store = AgentAccessOAuthStore.ResolveConnectionString(Master, Dedicated);

        Assert.Equal(Dedicated, store.ConnectionString);
        Assert.True(store.Dedicated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_dedicated_connection_string_the_master_connection_is_used(string? dedicated)
    {
        var store = AgentAccessOAuthStore.ResolveConnectionString(Master, dedicated);

        Assert.Equal(Master, store.ConnectionString);
        Assert.False(store.Dedicated);
    }

    [Fact]
    public void The_description_never_contains_a_connection_string()
    {
        var dedicated = AgentAccessOAuthStore.ResolveConnectionString(Master, Dedicated);
        var fallback = AgentAccessOAuthStore.ResolveConnectionString(Master, null);

        foreach (var text in new[] { dedicated.Description, fallback.Description })
        {
            Assert.DoesNotContain("secret", text);
            Assert.DoesNotContain("Password", text);
            Assert.DoesNotContain("Server=", text);
        }

        Assert.Contains("AgentAccessOAuthSQLConnection", fallback.Description);
    }
}
