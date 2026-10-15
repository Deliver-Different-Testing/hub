using Hub.Features.AgentAccess.Oauth;

namespace Hub.Tests.Features.AgentAccess.Oauth;

// Phase 1.5: the one resource value this Hub's tenant accepts on /connect/authorize.
public class AgentAccessResourceTests
{
    [Theory]
    [InlineData("https://hub.urgent.deliverdifferent.com", "https://mcp.urgent.deliverdifferent.com/mcp")]
    [InlineData("https://hub.urgent.deliverdifferent.com/", "https://mcp.urgent.deliverdifferent.com/mcp")]
    [InlineData("https://hub.urgent.staging.deliverdifferent.com", "https://mcp.urgent.staging.deliverdifferent.com/mcp")]
    [InlineData("https://HUB.urgent.deliverdifferent.com", "https://mcp.urgent.deliverdifferent.com/mcp")]
    [InlineData("https://hub.localtest.me:5001", "https://mcp.localtest.me:5001/mcp")]
    public void Derives_the_mcp_host_from_the_hub_issuer(string issuer, string expected)
    {
        Assert.Equal(expected, AgentAccessResource.Resolve(issuer, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://hub.urgent.deliverdifferent.com")]
    [InlineData("https://urgent.deliverdifferent.com")]
    [InlineData("https://hubby.urgent.deliverdifferent.com")]
    [InlineData("https://hub.urgent.deliverdifferent.com/some/path")]
    public void Is_null_when_the_issuer_is_not_a_plain_https_hub_origin(string? issuer)
    {
        Assert.Null(AgentAccessResource.Resolve(issuer, null));
    }

    [Fact]
    public void An_explicit_value_wins_over_the_derived_one()
    {
        var resolved = AgentAccessResource.Resolve("https://hub.urgent.deliverdifferent.com", "https://mcp.custom.example.test/mcp");

        Assert.Equal("https://mcp.custom.example.test/mcp", resolved);
    }

    [Fact]
    public void An_explicit_value_works_without_a_derivable_issuer()
    {
        Assert.Equal("https://mcp.custom.example.test/mcp", AgentAccessResource.Resolve(null, "https://mcp.custom.example.test/mcp"));
    }

    [Theory]
    [InlineData("http://mcp.custom.example.test/mcp")]
    [InlineData("mcp.custom.example.test")]
    [InlineData("https://mcp.custom.example.test/mcp#frag")]
    [InlineData("https://mcp.custom.example.test/mcp?x=1")]
    public void An_invalid_explicit_value_is_not_silently_replaced_by_the_derived_one(string value)
    {
        Assert.Null(AgentAccessResource.Resolve("https://hub.urgent.deliverdifferent.com", value));
    }
}
