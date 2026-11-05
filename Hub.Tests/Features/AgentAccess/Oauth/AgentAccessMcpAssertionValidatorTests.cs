using Hub.Features.AgentAccess.Oauth;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace Hub.Tests.Features.AgentAccess.Oauth;

// A cluster that cannot be reached refuses the MCP server's login; it never fails the request, and a caller that
// gives up still stops the work.
public class AgentAccessMcpAssertionValidatorTests
{
    private const string Cluster = "https://oidc.eks.ap-southeast-2.amazonaws.com/id/ABC";
    private const string Hub = "https://hub.urgent.example.test/";

    private sealed class CancelledFetch : BaseConfigurationManager
    {
        public override Task<BaseConfiguration> GetBaseConfigurationAsync(CancellationToken cancel) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        public override void RequestRefresh()
        {
        }
    }

    private sealed class Jwks(BaseConfigurationManager manager) : IAgentAccessClusterJwks
    {
        public BaseConfigurationManager? Manager { get; } = manager;
    }

    private static AgentAccessMcpAssertionValidator Validator() => new(
        AgentAccessMcpAssertionOptions.Resolve(Cluster, ["system:serviceaccount:mcp-1-staging:default"]), new Jwks(new CancelledFetch()));

    private static string Token()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var now = DateTime.UtcNow;
        return new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Cluster,
            Audience = Hub,
            Claims = new Dictionary<string, object> { ["sub"] = "system:serviceaccount:mcp-1-staging:default" },
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = "k" }, SecurityAlgorithms.RsaSha256)
        });
    }

    [Fact]
    public async Task A_key_fetch_that_times_out_is_a_refusal()
    {
        var result = await Validator().ValidateAsync(Token(), [Hub], TestContext.Current.CancellationToken);

        Assert.False(result.Accepted);
    }

    [Fact]
    public async Task A_cancelled_caller_still_cancels()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Validator().ValidateAsync(Token(), [Hub], cts.Token));
    }
}
