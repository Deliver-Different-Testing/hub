using Hub.Features.AgentAccess.Capabilities;
using Hub.Features.AgentAccess.Grants;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Capabilities;

// The apps ask this on agent requests (cached briefly) so a role change in Access Control applies within a minute,
// not when the app pass expires. Server to server, X-Api-Key as the other agent-access APIs.
[Collection("PartnerDirectoryApiKey")]
public class AgentAccessCapabilitiesApiControllerTests : IDisposable
{
    private const string Key = "shared-key";
    private readonly string? _original = Environment.GetEnvironmentVariable("PartnerDirectoryApiKey");
    private readonly IAgentPassIdentityLoader _identities = Substitute.For<IAgentPassIdentityLoader>();
    private readonly AgentAccessCapabilitiesApiController _sut;

    public AgentAccessCapabilitiesApiControllerTests()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", Key);
        _sut = new AgentAccessCapabilitiesApiController(_identities);
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
        Assert.IsType<UnauthorizedResult>(await _sut.Get(12, "55", 9, key, Ct));
        await _identities.DidNotReceiveWithAnyArgs().LoadAsync(0, null!, 0, Ct);
    }

    [Theory]
    [InlineData(0, "55", 9)]
    [InlineData(12, "", 9)]
    [InlineData(12, "55", 0)]
    public async Task An_incomplete_caller_is_a_bad_request(int tenantId, string userId, int contactId)
    {
        Assert.IsType<BadRequestResult>(await _sut.Get(tenantId, userId, contactId, Key, Ct));
    }

    [Fact]
    public async Task An_eligible_user_gets_their_capabilities()
    {
        _identities.LoadAsync(12, "55", 9, Arg.Any<CancellationToken>()).Returns(new AgentPassLoad(
            AgentGrantEligibility.Eligible, null, new HashSet<string> { AgentCapabilities.Connect, AgentCapabilities.ExportJobs }));

        var body = Assert.IsType<AgentCapabilitiesResponse>(Assert.IsType<OkObjectResult>(await _sut.Get(12, "55", 9, Key, Ct)).Value);

        Assert.Equal("Eligible", body.Eligibility);
        Assert.Equal([AgentCapabilities.Connect, AgentCapabilities.ExportJobs], body.Capabilities);
    }

    [Theory]
    [InlineData(AgentGrantEligibility.NotPermitted)]
    [InlineData(AgentGrantEligibility.TenantOff)]
    [InlineData(AgentGrantEligibility.NoLongerEligible)]
    public async Task Anyone_who_could_not_get_a_pass_has_no_capabilities(AgentGrantEligibility eligibility)
    {
        _identities.LoadAsync(12, "55", 9, Arg.Any<CancellationToken>()).Returns(new AgentPassLoad(eligibility, null));

        var body = Assert.IsType<AgentCapabilitiesResponse>(Assert.IsType<OkObjectResult>(await _sut.Get(12, "55", 9, Key, Ct)).Value);

        Assert.Equal(eligibility.ToString(), body.Eligibility);
        Assert.Empty(body.Capabilities);
    }

    [Fact]
    public async Task A_check_that_could_not_finish_is_a_503_so_callers_fail_closed_without_caching_it()
    {
        _identities.LoadAsync(12, "55", 9, Arg.Any<CancellationToken>()).Returns(new AgentPassLoad(AgentGrantEligibility.Unknown, null));

        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await _sut.Get(12, "55", 9, Key, Ct)).StatusCode);
    }
}
