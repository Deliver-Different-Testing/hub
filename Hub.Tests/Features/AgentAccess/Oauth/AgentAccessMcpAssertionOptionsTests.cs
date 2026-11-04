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

    private const string McpProjectPrefix = "system:serviceaccount:dfrnt-automate-mcp-87595416-*";

    [Fact]
    public void An_exact_entry_allows_only_that_subject()
    {
        var options = AgentAccessMcpAssertionOptions.Resolve(Cluster, [Account]);

        Assert.True(options.Allows(Account));
        Assert.False(options.Allows(Account + "x"));
        Assert.False(options.Allows("system:serviceaccount:dfrnt-automate-mcp-1-urgent-staging:other"));
    }

    [Theory]
    [InlineData("system:serviceaccount:dfrnt-automate-mcp-87595416-staging:default")]
    [InlineData("system:serviceaccount:dfrnt-automate-mcp-87595416-production:dfrnt-automate-mcp")]
    public void A_trailing_star_matches_every_subject_that_starts_with_the_rest(string subject)
    {
        var options = AgentAccessMcpAssertionOptions.Resolve(Cluster, [McpProjectPrefix]);

        Assert.True(options.Enabled);
        Assert.True(options.Allows(subject));
    }

    [Theory]
    [InlineData("system:serviceaccount:hub-12345-staging:default")]
    [InlineData("system:serviceaccount:dfrnt-automate-mcp-87595417-staging:default")]
    [InlineData("system:serviceaccount:dfrnt-automate-mcp-87595416:default")]
    [InlineData("system:serviceaccount:dfrnt-automate-mcp-87595416")]
    [InlineData("SYSTEM:SERVICEACCOUNT:DFRNT-AUTOMATE-MCP-87595416-STAGING:DEFAULT")]
    [InlineData("")]
    public void A_prefix_refuses_every_other_subject(string subject)
    {
        var options = AgentAccessMcpAssertionOptions.Resolve(Cluster, [McpProjectPrefix]);

        Assert.False(options.Allows(subject));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("system:serviceaccount:*")]
    [InlineData("system:serviceaccount:*:default")]
    [InlineData("system:serviceaccount:dfrnt-*-mcp*")]
    [InlineData("system:serviceaccount:dfrnt-automate-mcp-87595416-staging:*:*")]
    public void Patterns_other_than_one_trailing_star_after_a_namespace_are_ignored(string entry)
    {
        var options = AgentAccessMcpAssertionOptions.Resolve(Cluster, [entry]);

        Assert.False(options.Enabled);
        Assert.Empty(options.ServiceAccounts);
        Assert.False(options.Allows("system:serviceaccount:dfrnt-automate-mcp-87595416-staging:default"));
    }

    [Fact]
    public void The_shipped_default_allows_the_mcp_projects_namespaces_and_nothing_else()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();

        var options = AgentAccessMcpAssertionOptions.Resolve(
            configuration["AgentAccess:ClusterIssuer"], configuration.GetSection("AgentAccess:McpServiceAccounts").Get<string[]>());

        Assert.True(options.Enabled);
        Assert.Equal([McpProjectPrefix], options.ServiceAccounts);
        Assert.True(options.Allows("system:serviceaccount:dfrnt-automate-mcp-87595416-staging:default"));
        Assert.False(options.Allows("system:serviceaccount:hub-12345-staging:default"));
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
