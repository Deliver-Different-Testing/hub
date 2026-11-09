using Hub.Features.AiAssistant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Hub.Tests.Features.AiAssistant;

public class AiAssistantServiceExtensionsTests
{
    private static AiAssistantOptions Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);

        var services = new ServiceCollection().AddAiAssistant(configuration, environment);

        return services.BuildServiceProvider().GetRequiredService<AiAssistantOptions>();
    }

    [Fact]
    public void Federation_IsReadFromTheSharedAutoMateAnthropicSection()
    {
        var options = Register(new()
        {
            ["AutoMate:Anthropic:Federation:RuleId"] = "fdrl_hub",
            ["AutoMate:Anthropic:Federation:OrganizationId"] = "org",
            ["AutoMate:Anthropic:Federation:ServiceAccountId"] = "svac_hub",
            ["AiAssistant:Model"] = "claude-sonnet-5-5"
        });

        Assert.Equal("fdrl_hub", options.Federation.RuleId);
        Assert.Equal("org", options.Federation.OrganizationId);
        Assert.Equal("svac_hub", options.Federation.ServiceAccountId);
        Assert.Equal("/var/run/secrets/anthropic.com/token", options.Federation.IdentityTokenFile);
        Assert.Equal("claude-sonnet-5-5", options.Model);
    }

    [Fact]
    public void AnApiKey_NoLongerTurnsTheAssistantOn()
    {
        var options = Register(new() { ["AutoMate:Anthropic:ApiKey"] = "sk-shared", ["AiAssistant:ApiKey"] = "sk-old" });

        Assert.False(options.Enabled);
    }
}
