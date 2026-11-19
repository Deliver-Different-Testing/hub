using System.Security.Cryptography;
using Hub.Features.AgentAccess.Capabilities;
using Hub.Features.AgentAccess.Grants;
using Hub.Features.AgentAccess.Oauth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Capabilities;

// The apps ask this on agent requests (cached briefly) so a role change in Access Control applies within a minute,
// not when the app pass expires. Server to server: the caller's Kubernetes service-account token (audience
// dfrnt-hub-agent-access), or X-Api-Key while the apps move over.
[Collection("PartnerDirectoryApiKey")]
public class AgentAccessCapabilitiesApiControllerTests : IDisposable
{
    private const string Key = "shared-key";
    private const string Cluster = "https://oidc.eks.ap-southeast-2.amazonaws.com/id/ABC";
    private const string Configurator = "system:serviceaccount:dfrntdrive-configurator-804-urgent-staging:dfrntdrive-configurator";
    private const string DespatchWeb = "system:serviceaccount:despatchweb-594-urgent-staging:despatchweb";
    private static readonly RsaSecurityKey ClusterKey = new(RSA.Create(2048)) { KeyId = "k1" };
    private readonly string? _original = Environment.GetEnvironmentVariable("PartnerDirectoryApiKey");
    private readonly IAgentPassIdentityLoader _identities = Substitute.For<IAgentPassIdentityLoader>();
    private readonly AgentAccessCapabilitiesApiController _sut;

    public AgentAccessCapabilitiesApiControllerTests()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", Key);
        _sut = new AgentAccessCapabilitiesApiController(_identities, new AgentAccessCapabilitiesTokenValidator(
            new AgentAccessCapabilitiesTokenOptions(AgentAccessMcpAssertionOptions.Resolve(
                Cluster, ["system:serviceaccount:dfrntdrive-configurator-804-*", "system:serviceaccount:despatchweb-594-*"])),
            new StaticJwks()));
    }

    private static string Bearer(string subject = Configurator, string audience = AgentAccessCapabilitiesTokenValidator.Audience) =>
        "Bearer " + new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Cluster,
            Audience = audience,
            Claims = new Dictionary<string, object> { ["sub"] = subject },
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(ClusterKey, SecurityAlgorithms.RsaSha256)
        });

    private sealed class StaticJwks : IAgentAccessClusterJwks
    {
        public BaseConfigurationManager? Manager { get; } = new StaticConfigurationManager<OpenIdConnectConfiguration>(
            new OpenIdConnectConfiguration { Issuer = Cluster, SigningKeys = { ClusterKey } });
    }

    private void Eligible() => _identities.LoadAsync(12, "55", 9, Arg.Any<CancellationToken>()).Returns(new AgentPassLoad(
        AgentGrantEligibility.Eligible, null, new HashSet<string> { AgentCapabilities.Connect }));

    [Theory]
    [InlineData(Configurator)]
    [InlineData(DespatchWeb)]
    public async Task An_allowed_apps_service_account_token_is_enough_without_the_key(string subject)
    {
        Eligible();

        var body = Assert.IsType<AgentCapabilitiesResponse>(
            Assert.IsType<OkObjectResult>(await _sut.Get(12, "55", 9, null, Bearer(subject), Ct)).Value);

        Assert.Equal([AgentCapabilities.Connect], body.Capabilities);
    }

    public static TheoryData<string> RefusedTokens() => new()
    {
        // The admin APIs' token must not be replayable here, nor this one there.
        Bearer(audience: AgentAccessAdminTokenValidator.Audience),
        Bearer(subject: "system:serviceaccount:dfrnt-automate-mcp-87595416-urgent-staging:mcp"),
        "Bearer not.a.jwt",
    };

    [Theory]
    [MemberData(nameof(RefusedTokens))]
    public async Task A_token_for_another_audience_or_service_account_is_refused_and_nothing_is_read(string authorization)
    {
        Assert.IsType<UnauthorizedResult>(await _sut.Get(12, "55", 9, null, authorization, Ct));
        await _identities.DidNotReceiveWithAnyArgs().LoadAsync(0, null!, 0, Ct);
    }

    [Fact]
    public async Task A_refused_token_is_not_rescued_by_a_valid_key()
    {
        Assert.IsType<UnauthorizedResult>(await _sut.Get(12, "55", 9, Key, Bearer(audience: AgentAccessAdminTokenValidator.Audience), Ct));
        await _identities.DidNotReceiveWithAnyArgs().LoadAsync(0, null!, 0, Ct);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", _original);
        GC.SuppressFinalize(this);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task A_missing_or_wrong_key_is_refused_and_nothing_is_read(string? key)
    {
        Assert.IsType<UnauthorizedResult>(await _sut.Get(12, "55", 9, key, null, Ct));
        await _identities.DidNotReceiveWithAnyArgs().LoadAsync(0, null!, 0, Ct);
    }

    [Theory]
    [InlineData(0, "55", 9)]
    [InlineData(12, "", 9)]
    [InlineData(12, "55", 0)]
    public async Task An_incomplete_caller_is_a_bad_request(int tenantId, string userId, int contactId)
    {
        Assert.IsType<BadRequestResult>(await _sut.Get(tenantId, userId, contactId, Key, null, Ct));
    }

    [Fact]
    public async Task An_eligible_user_gets_their_capabilities()
    {
        _identities.LoadAsync(12, "55", 9, Arg.Any<CancellationToken>()).Returns(new AgentPassLoad(
            AgentGrantEligibility.Eligible, null, new HashSet<string> { AgentCapabilities.Connect, AgentCapabilities.ExportJobs }));

        var body = Assert.IsType<AgentCapabilitiesResponse>(Assert.IsType<OkObjectResult>(await _sut.Get(12, "55", 9, Key, null, Ct)).Value);

        Assert.Equal("Eligible", body.Eligibility);
        Assert.Equal([AgentCapabilities.Connect, AgentCapabilities.ExportJobs], body.Capabilities);
    }

    [Fact]
    public async Task An_eligible_user_also_gets_each_tools_verb_screen_and_reason()
    {
        var tools = new Dictionary<string, AgentToolDecision>
        {
            ["agent-tool-job-search"] = new(true, false, "dw-job-search is off for this role")
        };
        _identities.LoadAsync(12, "55", 9, Arg.Any<CancellationToken>()).Returns(new AgentPassLoad(
            AgentGrantEligibility.Eligible, null, new HashSet<string> { AgentCapabilities.Connect }, tools));

        var body = Assert.IsType<AgentCapabilitiesResponse>(Assert.IsType<OkObjectResult>(await _sut.Get(12, "55", 9, Key, null, Ct)).Value);

        Assert.Equal(new AgentToolDecision(true, false, "dw-job-search is off for this role"), body.Tools!["agent-tool-job-search"]);
    }

    [Fact]
    public void The_response_serialises_tools_as_verb_screen_reason()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new AgentCapabilitiesResponse("Eligible", [], new Dictionary<string, AgentToolDecision> { ["v"] = new(true, false, "why") }),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Equal("""{"eligibility":"Eligible","capabilities":[],"tools":{"v":{"verb":true,"screen":false,"reason":"why"}}}""", json);
    }

    [Theory]
    [InlineData(AgentGrantEligibility.NotPermitted)]
    [InlineData(AgentGrantEligibility.TenantOff)]
    [InlineData(AgentGrantEligibility.NoLongerEligible)]
    public async Task Anyone_who_could_not_get_a_pass_has_no_capabilities(AgentGrantEligibility eligibility)
    {
        _identities.LoadAsync(12, "55", 9, Arg.Any<CancellationToken>()).Returns(new AgentPassLoad(eligibility, null));

        var body = Assert.IsType<AgentCapabilitiesResponse>(Assert.IsType<OkObjectResult>(await _sut.Get(12, "55", 9, Key, null, Ct)).Value);

        Assert.Equal(eligibility.ToString(), body.Eligibility);
        Assert.Empty(body.Capabilities);
    }

    [Fact]
    public async Task A_check_that_could_not_finish_is_a_503_so_callers_fail_closed_without_caching_it()
    {
        _identities.LoadAsync(12, "55", 9, Arg.Any<CancellationToken>()).Returns(new AgentPassLoad(AgentGrantEligibility.Unknown, null));

        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await _sut.Get(12, "55", 9, Key, null, Ct)).StatusCode);
    }
}
