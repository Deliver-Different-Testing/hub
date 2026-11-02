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
    public void ApiKey_IsReadFromTheSharedAutoMateAnthropicKey()
    {
        var options = Register(new() { ["AutoMate:Anthropic:ApiKey"] = "sk-shared", ["AiAssistant:Model"] = "claude-sonnet-5-5" });

        Assert.Equal("sk-shared", options.ApiKey);
        Assert.True(options.Enabled);
        Assert.Equal("claude-sonnet-5-5", options.Model);
    }

    [Fact]
    public void WithoutTheSharedKey_TheAssistantIsOff()
    {
        var options = Register(new() { ["AiAssistant:ApiKey"] = "sk-old" });

        Assert.False(options.Enabled);
    }
}
