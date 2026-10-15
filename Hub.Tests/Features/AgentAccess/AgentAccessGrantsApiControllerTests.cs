using Hub.Features.AgentAccess;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess;

[Collection("PartnerDirectoryApiKey")]
public class AgentAccessGrantsApiControllerTests : IDisposable
{
    private const string Key = "shared-key";
    private readonly string? _original = Environment.GetEnvironmentVariable("PartnerDirectoryApiKey");
    private readonly IAgentGrantAdmin _admin = Substitute.For<IAgentGrantAdmin>();
    private readonly IAgentGrantStore _store = Substitute.For<IAgentGrantStore>();
    private readonly AgentAccessGrantsApiController _sut;

    public AgentAccessGrantsApiControllerTests()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", Key);
        _sut = new AgentAccessGrantsApiController(_admin, _store);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", _original);
        GC.SuppressFinalize(this);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("shared-ke")]
    [InlineData("shared-keyx")]
    public async Task A_missing_or_wrong_key_is_refused_and_nothing_is_read_or_changed(string? key)
    {
        Assert.IsType<UnauthorizedResult>(await _sut.List(12, "55", false, key, Ct));
        Assert.IsType<UnauthorizedResult>(await _sut.Revoke("x", new AgentGrantRevokeRequest(12, "55", false), key, Ct));
        await _admin.DidNotReceiveWithAnyArgs().ListAsync(default, default!, default, default);
        await _admin.DidNotReceiveWithAnyArgs().RevokeAsync(default!, default, default!, default, default);
    }

    [Fact]
    public async Task No_key_is_configured_means_nothing_is_allowed()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", "");

        Assert.IsType<UnauthorizedResult>(await _sut.List(12, "55", false, "", Ct));
    }

    [Fact]
    public async Task List_passes_the_tenant_user_and_scope_through()
    {
        var grants = new List<AgentGrantSummary> { new("id-1", "55", "Claude", DateTimeOffset.UnixEpoch) };
        _admin.ListAsync(12, "55", true, Arg.Any<CancellationToken>()).Returns(grants);

        var result = await _sut.List(12, "55", true, Key, Ct);

        Assert.Same(grants, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Theory]
    [InlineData(0, "55")]
    [InlineData(-1, "55")]
    [InlineData(12, "")]
    [InlineData(12, null)]
    public async Task List_needs_a_tenant_and_a_user(int tenantId, string? userId)
    {
        Assert.IsType<BadRequestResult>(await _sut.List(tenantId, userId, false, Key, Ct));
    }

    [Fact]
    public async Task Revoke_returns_no_content_when_revoked_and_not_found_otherwise()
    {
        _admin.RevokeAsync("good", 12, "55", false, Arg.Any<CancellationToken>()).Returns(AgentGrantRevokeResult.Revoked);
        _admin.RevokeAsync("bad", 12, "55", false, Arg.Any<CancellationToken>()).Returns(AgentGrantRevokeResult.NotFound);

        Assert.IsType<NoContentResult>(await _sut.Revoke("good", new AgentGrantRevokeRequest(12, "55", false), Key, Ct));
        Assert.IsType<NotFoundResult>(await _sut.Revoke("bad", new AgentGrantRevokeRequest(12, "55", false), Key, Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Setting_a_tenant_off_or_on_writes_the_flag(bool off)
    {
        var result = await _sut.SetTenantOff(12, new AgentAccessTenantOffRequest(off), Key, Ct);

        Assert.IsType<NoContentResult>(result);
        await _store.Received(1).SetTenantOffAsync(12, off, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task The_tenant_flag_needs_the_key_and_a_real_tenant(string? key)
    {
        Assert.IsType<UnauthorizedResult>(await _sut.SetTenantOff(12, new AgentAccessTenantOffRequest(true), key, Ct));
        Assert.IsType<BadRequestResult>(await _sut.SetTenantOff(0, new AgentAccessTenantOffRequest(true), Key, Ct));
        await _store.DidNotReceiveWithAnyArgs().SetTenantOffAsync(default, default, default);
    }

    [Fact]
    public async Task Revoke_needs_a_tenant_and_a_user()
    {
        Assert.IsType<BadRequestResult>(await _sut.Revoke("x", new AgentGrantRevokeRequest(0, "55", false), Key, Ct));
        Assert.IsType<BadRequestResult>(await _sut.Revoke("x", new AgentGrantRevokeRequest(12, " ", false), Key, Ct));
    }
}
