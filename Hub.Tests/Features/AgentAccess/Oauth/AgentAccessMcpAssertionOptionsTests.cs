using Hub.Features.AgentAccess;
using Hub.Features.AgentAccess.Keys;
using Hub.Features.AgentAccess.Oauth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hub.Tests.Features.AgentAccess.Oauth;

// AgentAccess:ClusterIssuer and AgentAccess:McpServiceAccounts switch the MCP server's service-account login on;
// anything unusable leaves it off rather than half on.
public class AgentAccessMcpAssertionOptionsTests
{
    private const string Cluster = "https://oidc.eks.ap-southeast-2.amazonaws.com/id/ABC";
    private const string Account = "system:serviceaccount:dfrnt-automate-mcp-1-urgent-staging:default";

    [Fact]
    public void Both_set_turns_it_on()
    {
        var options = AgentAccessMcpAssertionOptions.Resolve(Cluster, [Account]);

        Assert.True(options.Enabled);
        Assert.Equal(new Uri(Cluster), options.ClusterIssuer);
        Assert.Equal([Account], options.ServiceAccounts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://oidc.eks.ap-southeast-2.amazonaws.com/id/ABC")]
    [InlineData("not a url")]
    public void Without_a_usable_https_issuer_it_is_off(string? issuer)
    {
        Assert.False(AgentAccessMcpAssertionOptions.Resolve(issuer, [Account]).Enabled);
    }

    [Fact]
    public void Without_service_accounts_it_is_off()
    {
        Assert.False(AgentAccessMcpAssertionOptions.Resolve(Cluster, null).Enabled);
        Assert.False(AgentAccessMcpAssertionOptions.Resolve(Cluster, []).Enabled);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("system:serviceaccount:ns")]
    [InlineData("system:serviceaccount:ns:sa:extra")]
    [InlineData("system:serviceaccounts:ns")]
    public void Entries_that_are_not_a_service_account_subject_are_ignored(string entry)
    {
        var options = AgentAccessMcpAssertionOptions.Resolve(Cluster, [entry, " " + Account + " "]);

        Assert.Equal([Account], options.ServiceAccounts);
    }

    [Theory]
    [InlineData("mcp-secret", null, true, false)]
    [InlineData(null, Account, false, true)]
    [InlineData("mcp-secret", Account, true, true)]
    [InlineData(null, null, false, false)]
    public void The_exchange_client_is_configured_by_a_secret_or_the_assertion(string? secret, string? account, bool bySecret, bool byAssertion)
    {
        var services = new ServiceCollection();
        services.AddAgentAccessOAuth("Server=unused;", new AgentAccessOAuthSettings(
            McpClientSecret: secret, ClusterIssuer: Cluster, McpServiceAccounts: account is null ? [] : [account]));

        var exchange = services.BuildServiceProvider().GetRequiredService<AgentAccessMcpExchange>();

        Assert.Equal(bySecret, exchange.SecretConfigured);
        Assert.Equal(byAssertion, exchange.AssertionConfigured);
        Assert.Equal(bySecret || byAssertion, exchange.Configured);
    }

    [Fact]
    public void The_cluster_issuer_shipped_in_appsettings_is_usable()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();

        var options = AgentAccessMcpAssertionOptions.Resolve(configuration["AgentAccess:ClusterIssuer"], [Account]);

        Assert.True(options.Enabled);
    }
}
