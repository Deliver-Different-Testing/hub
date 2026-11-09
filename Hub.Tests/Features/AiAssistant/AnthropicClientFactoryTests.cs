using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic.Credentials;
using Anthropic.Models.Messages;
using Hub.Features.AiAssistant;

namespace Hub.Tests.Features.AiAssistant;

public sealed class AnthropicClientFactoryTests : IDisposable
{
    private const string IdentityJwt = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJzeXN0ZW06c2VydmljZWFjY291bnQ6aHViOmRlZmF1bHQifQ.sig";
    private const string AccessToken = "sk-ant-oat01-federated";

    private readonly string _tokenFile = Path.GetTempFileName();
    private readonly RecordingHandler _handler = new();

    public AnthropicClientFactoryTests() => File.WriteAllText(_tokenFile, IdentityJwt);

    public void Dispose() => File.Delete(_tokenFile);

    private AiAssistantOptions.FederationOptions Federation() => new()
    {
        RuleId = "fdrl_test",
        OrganizationId = "00000000-0000-0000-0000-000000000001",
        ServiceAccountId = "svac_test",
        WorkspaceId = "wrkspc_test",
        IdentityTokenFile = _tokenFile
    };

    private static MessageCreateParams Request() => new()
    {
        Model = "claude-haiku-5-5",
        MaxTokens = 16,
        Messages = [new UserMessageParam { Content = "hi" }]
    };

    [Fact]
    public async Task Create_WithFederation_ExchangesIdentityTokenAndSendsBearer()
    {
        var client = AnthropicClientFactory.Create(
            new AiAssistantOptions { Federation = Federation(), MaxRetries = 0 }, new HttpClient(_handler));

        await client.Messages.Create(Request(), TestContext.Current.CancellationToken);

        var exchange = Assert.Single(_handler.Requests, r => r.Path == "/v1/oauth/token");
        using var body = JsonDocument.Parse(exchange.Body);
        Assert.Equal("urn:ietf:params:oauth:grant-type:jwt-bearer", body.RootElement.GetProperty("grant_type").GetString());
        Assert.Equal(IdentityJwt, body.RootElement.GetProperty("assertion").GetString());
        Assert.Equal("fdrl_test", body.RootElement.GetProperty("federation_rule_id").GetString());
        Assert.Equal("00000000-0000-0000-0000-000000000001", body.RootElement.GetProperty("organization_id").GetString());
        Assert.Equal("svac_test", body.RootElement.GetProperty("service_account_id").GetString());
        Assert.Equal("wrkspc_test", body.RootElement.GetProperty("workspace_id").GetString());

        var message = Assert.Single(_handler.Requests, r => r.Path == "/v1/messages");
        Assert.Equal($"Bearer {AccessToken}", message.Authorization);
        Assert.Null(message.ApiKeyHeader);
    }

    [Fact]
    public void Create_AppliesTheConfiguredTimeoutAndRetries()
    {
        var client = AnthropicClientFactory.Create(new AiAssistantOptions { Federation = Federation(), TimeoutSeconds = 45, MaxRetries = 1 });

        Assert.Equal(TimeSpan.FromSeconds(45), client.Timeout);
        Assert.Equal(1, client.MaxRetries);
    }

    [Fact]
    public void Enabled_RequiresCompleteFederationAndAnIdentityTokenFile()
    {
        Assert.False(new AiAssistantOptions().Enabled);
        Assert.True(new AiAssistantOptions { Federation = Federation() }.Enabled);
        Assert.False(new AiAssistantOptions { Federation = new() { RuleId = "fdrl_test", IdentityTokenFile = _tokenFile } }.Enabled);

        var missingFile = Federation();
        missingFile.IdentityTokenFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.False(new AiAssistantOptions { Federation = missingFile }.Enabled);
    }

    [Fact]
    public async Task Create_WithoutFederation_NeverFallsBackToAnApiKeyInTheEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-api03-leftover");
        try
        {
            var client = AnthropicClientFactory.Create(new AiAssistantOptions { MaxRetries = 0 }, new HttpClient(_handler));

            await client.Messages.Create(Request(), TestContext.Current.CancellationToken);

            var message = Assert.Single(_handler.Requests);
            Assert.NotEqual("sk-ant-api03-leftover", message.ApiKeyHeader);
            Assert.Equal("/v1/messages", message.Path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previous);
        }
    }

    [Fact]
    public void AMissingOrRefusedIdentityToken_MeansNotConnected()
    {
        Assert.Equal(LlmFailure.NotConnected, AnthropicErrors.Classify(new FileNotFoundException("token")));
        Assert.Equal(LlmFailure.NotConnected, AnthropicErrors.Classify(new DirectoryNotFoundException("token")));
        Assert.Equal(LlmFailure.NotConnected, AnthropicErrors.Classify(new WorkloadIdentityException("invalid_grant")));
    }

    private sealed record RecordedRequest(string Path, string Body, string? Authorization, string? ApiKeyHeader);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsolutePath,
                body,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("x-api-key", out var key) ? key.Single() : null));

            var json = request.RequestUri.AbsolutePath == "/v1/oauth/token"
                ? $$"""{"access_token":"{{AccessToken}}","token_type":"Bearer","expires_in":600}"""
                : """
                  {"id":"msg_1","type":"message","role":"assistant","model":"claude-haiku-5-5",
                   "content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","stop_sequence":null,
                   "usage":{"input_tokens":1,"output_tokens":1}}
                  """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}

/// <summary>A federation with a real identity-token file, so <see cref="AiAssistantOptions.Enabled"/> is true.</summary>
internal static class TestFederation
{
    private static readonly string TokenFile = CreateTokenFile();

    public static AiAssistantOptions.FederationOptions Connected() => new()
    {
        RuleId = "fdrl_test",
        OrganizationId = "00000000-0000-0000-0000-000000000001",
        ServiceAccountId = "svac_test",
        IdentityTokenFile = TokenFile
    };

    private static string CreateTokenFile()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "jwt");
        return path;
    }
}
