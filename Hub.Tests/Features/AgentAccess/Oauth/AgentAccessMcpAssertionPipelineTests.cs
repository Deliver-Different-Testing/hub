using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

namespace Hub.Tests.Features.AgentAccess.Oauth;

// The MCP server authenticates its token exchange with its Kubernetes projected service-account token (RFC 7523
// client assertion) instead of a shared secret. Hub checks it against the cluster's OIDC keys; every other client
// authenticates exactly as before.
public partial class AgentAccessAuthorizePipelineTests
{
    private const string ClusterIssuer = "https://oidc.eks.ap-southeast-2.amazonaws.com/id/TESTCLUSTER";
    private const string McpServiceAccount = "system:serviceaccount:dfrnt-automate-mcp-1-staging:default";
    private const string JwtBearer = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private static readonly RsaSecurityKey ClusterKey = NewClusterKey("cluster-key-1");

    private static RsaSecurityKey NewClusterKey(string kid) => new(RSA.Create(2048)) { KeyId = kid };

    private static string ServiceAccountToken(
        string issuer = ClusterIssuer, string audience = Issuer, string subject = McpServiceAccount,
        DateTime? issuedAt = null, TimeSpan? lifetime = null, SecurityKey? key = null, string algorithm = SecurityAlgorithms.RsaSha256)
    {
        var now = issuedAt ?? DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = new Dictionary<string, object> { ["sub"] = subject },
            IssuedAt = now,
            NotBefore = now,
            Expires = now + (lifetime ?? TimeSpan.FromMinutes(10)),
            SigningCredentials = new SigningCredentials(key ?? ClusterKey, algorithm)
        });
    }

    private static Task<HttpResponseMessage> AssertionExchange(
        WebApplication app, string? subjectToken, string assertion, string clientId = "dfrnt-mcp", string assertionType = JwtBearer)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = ExchangeGrant,
            ["client_id"] = clientId,
            ["client_assertion_type"] = assertionType,
            ["client_assertion"] = assertion,
            ["subject_token_type"] = AccessTokenType
        };
        if (subjectToken is not null) form["subject_token"] = subjectToken;
        return Client(app).PostAsync("/connect/token", new FormUrlEncodedContent(form), Ct);
    }

    private const string RefusedByHub = "The client assertion is not valid.";

    private static Task AssertUnauthenticated(HttpResponseMessage response) =>
        AssertRefused(response, "invalid_client", HttpStatusCode.Unauthorized);

    [Fact]
    public async Task The_mcp_server_exchanges_with_its_service_account_token_and_no_secret_at_all()
    {
        var (app, tokens, _) = await ConnectedAsync(mcpSecret: null);
        await using var _ = app;

        var pass = await AppPass(await AssertionExchange(app, tokens.Access, ServiceAccountToken()));

        Assert.Contains("dfrnt-apps", pass.Audiences);
        Assert.Equal("55", pass.Subject);
        Assert.Equal("dfrnt-mcp", JsonDocument.Parse(pass.Claims.Single(c => c.Type == "act").Value).RootElement.GetProperty("client_id").GetString());
    }

    [Theory]
    [InlineData(Issuer)]
    [InlineData(Issuer + "/")]
    public async Task The_audience_is_this_hubs_issuer_with_or_without_a_trailing_slash(string audience)
    {
        var (app, tokens, _) = await ConnectedAsync(mcpSecret: null);
        await using var _ = app;

        await AppPass(await AssertionExchange(app, tokens.Access, ServiceAccountToken(audience: audience)));
    }

    [Fact]
    public async Task During_rollout_the_secret_and_the_assertion_both_work()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;

        await AppPass(await TokenExchange(app, tokens.Access));
        await AppPass(await AssertionExchange(app, tokens.Access, ServiceAccountToken()));
    }

    [Fact]
    public async Task With_only_the_assertion_configured_no_secret_authenticates_the_mcp_client()
    {
        var (app, tokens, _) = await ConnectedAsync(mcpSecret: null);
        await using var _ = app;

        await AssertUnauthenticated(await TokenExchange(app, tokens.Access, secret: "mcp-secret"));
        await AssertUnauthenticated(await TokenExchange(app, tokens.Access, secret: null));
        await AssertUnauthenticated(await TokenExchange(app, tokens.Access, secret: ""));
    }

    public static TheoryData<string, string> RefusedAssertions => new()
    {
        { "audience of another hub", nameof(OtherHubAudience) },
        { "audience of the cluster api", nameof(KubernetesAudience) },
        { "foreign issuer", nameof(ForeignIssuer) },
        { "unknown service account", nameof(UnknownServiceAccount) },
        { "service account in another namespace", nameof(OtherNamespace) },
        { "expired", nameof(Expired) },
        { "not yet valid", nameof(NotYetValid) },
        { "signed by another key with the cluster kid", nameof(BadSignature) },
        { "signed by a key the cluster never published", nameof(UnknownKey) },
        { "lifetime longer than an hour", nameof(TooLongLived) },
        { "not RS256", nameof(WrongAlgorithm) },
        { "not a jwt", nameof(Garbage) },
        { "unsigned", nameof(Unsigned) }
    };

    private static string OtherHubAudience() => ServiceAccountToken(audience: "https://hub.other-tenant.example.test");
    private static string KubernetesAudience() => ServiceAccountToken(audience: "https://kubernetes.default.svc");
    private static string ForeignIssuer() => ServiceAccountToken(issuer: "https://oidc.eks.ap-southeast-2.amazonaws.com/id/OTHERCLUSTER");
    private static string UnknownServiceAccount() => ServiceAccountToken(subject: "system:serviceaccount:dfrnt-automate-mcp-1-staging:intruder");
    private static string OtherNamespace() => ServiceAccountToken(subject: "system:serviceaccount:hub-2-staging:default");
    private static string Expired() => ServiceAccountToken(issuedAt: DateTime.UtcNow.AddMinutes(-20));
    private static string NotYetValid() => ServiceAccountToken(issuedAt: DateTime.UtcNow.AddMinutes(5));
    private static string BadSignature() => ServiceAccountToken(key: NewClusterKey(ClusterKey.KeyId));
    private static string UnknownKey() => ServiceAccountToken(key: NewClusterKey("never-published"));
    private static string TooLongLived() => ServiceAccountToken(lifetime: TimeSpan.FromHours(2));
    private static string WrongAlgorithm() => ServiceAccountToken(algorithm: SecurityAlgorithms.RsaSha512);
    private static string Garbage() => "not-a-token";

    private static string Unsigned()
    {
        static string Part(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        return Part("{\"alg\":\"none\",\"kid\":\"cluster-key-1\"}") + "."
               + Part($"{{\"iss\":\"{ClusterIssuer}\",\"aud\":\"{Issuer}\",\"sub\":\"{McpServiceAccount}\",\"exp\":{exp}}}") + ".";
    }

    [Theory]
    [MemberData(nameof(RefusedAssertions))]
    public async Task A_bad_service_account_token_is_invalid_client(string because, string factory)
    {
        var (app, tokens, _) = await ConnectedAsync(mcpSecret: null);
        await using var _ = app;
        var assertion = (string)typeof(AgentAccessAuthorizePipelineTests)
            .GetMethod(factory, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null)!;

        var response = await AssertionExchange(app, tokens.Access, assertion);

        // Refused by Hub's own check against the cluster keys, not by OpenIddict's fallback.
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, because);
        Assert.Contains(RefusedByHub, await response.Content.ReadAsStringAsync(Ct));
        await AssertUnauthenticated(response);
    }

    [Fact]
    public async Task With_the_cluster_issuer_unset_the_assertion_is_refused_and_the_secret_still_works()
    {
        var (app, tokens, _) = await ConnectedAsync(clusterIssuer: null);
        await using var _ = app;

        await AssertUnauthenticated(await AssertionExchange(app, tokens.Access, ServiceAccountToken()));
        await AppPass(await TokenExchange(app, tokens.Access));
        Assert.Equal(0, app.Services.GetRequiredService<ClusterOidcStub>().Requests);
    }

    [Fact]
    public async Task With_neither_secret_nor_assertion_configured_the_exchange_does_not_exist()
    {
        await using var app = await StartAsync(mcpSecret: null, clusterIssuer: null);

        await AssertUnauthenticated(await AssertionExchange(app, "anything", ServiceAccountToken()));
    }

    [Fact]
    public async Task A_valid_service_account_token_does_not_authenticate_any_other_client()
    {
        var (app, tokens, _) = await ConnectedAsync(mcpSecret: null);
        await using var _ = app;

        // The public AI app and an unknown client get exactly the answer OpenIddict always gave them.
        foreach (var clientId in new[] { "dfrnt-claude", "nobody" })
        {
            var response = await AssertionExchange(app, tokens.Access, ServiceAccountToken(), clientId: clientId);
            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain(RefusedByHub, body);
            await AssertRefused(response, "invalid_client", HttpStatusCode.Unauthorized);
        }
        Assert.Equal(0, app.Services.GetRequiredService<ClusterOidcStub>().Requests);
    }

    [Fact]
    public async Task The_assertion_cannot_be_sent_alongside_a_secret()
    {
        var (app, tokens, _) = await ConnectedAsync();
        await using var _ = app;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = ExchangeGrant,
            ["client_id"] = "dfrnt-mcp",
            ["client_secret"] = "mcp-secret",
            ["client_assertion_type"] = JwtBearer,
            ["client_assertion"] = ServiceAccountToken(),
            ["subject_token"] = tokens.Access,
            ["subject_token_type"] = AccessTokenType
        };

        var response = await Client(app).PostAsync("/connect/token", new FormUrlEncodedContent(form), Ct);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_rotated_cluster_key_is_picked_up_without_a_restart()
    {
        var (app, tokens, _) = await ConnectedAsync(mcpSecret: null);
        await using var _ = app;
        await AppPass(await AssertionExchange(app, tokens.Access, ServiceAccountToken()));

        var rotated = NewClusterKey("cluster-key-2");
        app.Services.GetRequiredService<ClusterOidcStub>().Keys.Add(rotated);

        // The unknown kid makes the configuration manager refetch the key set; allow it a moment to land.
        HttpResponseMessage response = null!;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            response = await AssertionExchange(app, tokens.Access, ServiceAccountToken(key: rotated));
            if (response.StatusCode == HttpStatusCode.OK) break;
            await Task.Delay(100, Ct);
        }

        await AppPass(response);
    }

    // The cluster's OIDC discovery document and key set, as EKS serves them.
    private sealed class ClusterOidcStub : HttpMessageHandler
    {
        private int _requests;

        public List<RsaSecurityKey> Keys { get; } = [ClusterKey];

        public int Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            var url = request.RequestUri!.AbsoluteUri;
            string? body = null;
            if (url == ClusterIssuer + "/.well-known/openid-configuration")
            {
                body = JsonSerializer.Serialize(new
                {
                    issuer = ClusterIssuer,
                    jwks_uri = ClusterIssuer + "/keys",
                    id_token_signing_alg_values_supported = new[] { "RS256" }
                });
            }
            else if (url == ClusterIssuer + "/keys")
            {
                body = JsonSerializer.Serialize(new
                {
                    keys = Keys.Select(k =>
                    {
                        var p = k.Rsa.ExportParameters(false);
                        return new { kty = "RSA", use = "sig", alg = "RS256", kid = k.KeyId, n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) };
                    })
                });
            }

            var response = body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json")) };
            return Task.FromResult(response);
        }
    }
}
