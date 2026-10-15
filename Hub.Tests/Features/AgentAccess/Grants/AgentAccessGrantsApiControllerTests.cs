using Hub.Features.AgentAccess.Grants;
using Hub.Interfaces;
using Hub.Models.Master;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Grants;

[Collection("PartnerDirectoryApiKey")]
public class AgentAccessGrantsApiControllerTests : IDisposable
{
    private const string Key = "shared-key";
    private readonly string? _original = Environment.GetEnvironmentVariable("PartnerDirectoryApiKey");
    private readonly IAgentGrantAdmin _admin = Substitute.For<IAgentGrantAdmin>();
    private readonly IAgentGrantStore _store = Substitute.For<IAgentGrantStore>();
    private readonly IAuthenticationRepository _master = Substitute.For<IAuthenticationRepository>();
    private readonly AgentAccessGrantsApiController _sut;

    public AgentAccessGrantsApiControllerTests()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", Key);
        _sut = new AgentAccessGrantsApiController(_admin, _store, _master);
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

    [Fact]
    public async Task Switching_a_tenant_off_raises_the_flag()
    {
        var result = await _sut.SetTenantOff(12, Key, Ct);

        Assert.IsType<NoContentResult>(result);
        await _store.Received(1).SetTenantOffAsync(12, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task The_tenant_flag_needs_the_key_and_a_real_tenant(string? key)
    {
        Assert.IsType<UnauthorizedResult>(await _sut.SetTenantOff(12, key, Ct));
        Assert.IsType<BadRequestResult>(await _sut.SetTenantOff(0, Key, Ct));
        await _store.DidNotReceiveWithAnyArgs().SetTenantOffAsync(default, default);
    }

    [Fact]
    public void The_api_offers_no_way_to_clear_the_flag()
    {
        var methods = typeof(AgentAccessGrantsApiController).GetMethods().Select(m => m.Name);

        Assert.DoesNotContain(methods, n => n.Contains("Clear") || n.Contains("On") && n.Contains("Tenant"));
    }

    [Fact]
    public async Task New_connections_come_with_the_address_to_tell()
    {
        _admin.ListUnnotifiedAsync(12, Arg.Any<CancellationToken>()).Returns(
        [
            new AgentConnection("g1", "55", "Claude", DateTimeOffset.UnixEpoch),
            new AgentConnection("g2", "999", "Claude", null),
            new AgentConnection("g3", "not-a-number", "ChatGPT", null)
        ]);
        _master.GetUserByIdAsync(55).Returns(new User { UserId = 55, Email = "staff@urgent.test" });
        _master.GetUserByIdAsync(999).Returns((User?)null);

        var result = Assert.IsType<OkObjectResult>(await _sut.Unnotified(12, Key, Ct));

        var notices = Assert.IsAssignableFrom<List<AgentConnectionNotice>>(result.Value);
        Assert.Equal(["staff@urgent.test", null, null], notices.Select(n => n.Email).ToArray());
        Assert.Equal(["g1", "g2", "g3"], notices.Select(n => n.Id).ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task The_notification_endpoints_need_the_key_and_a_real_tenant(string? key)
    {
        Assert.IsType<UnauthorizedResult>(await _sut.Unnotified(12, key, Ct));
        Assert.IsType<UnauthorizedResult>(await _sut.Notified("g1", new AgentConnectionNotifiedRequest(12, true), key, Ct));
        Assert.IsType<BadRequestResult>(await _sut.Unnotified(0, Key, Ct));
        Assert.IsType<BadRequestResult>(await _sut.Notified("g1", new AgentConnectionNotifiedRequest(0, true), Key, Ct));
        await _admin.DidNotReceiveWithAnyArgs().ListUnnotifiedAsync(default, default);
        await _admin.DidNotReceiveWithAnyArgs().MarkNotifiedAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Marking_a_connection_notified_is_no_content_or_not_found()
    {
        _admin.MarkNotifiedAsync("good", 12, true, Arg.Any<CancellationToken>()).Returns(true);
        _admin.MarkNotifiedAsync("bad", 12, true, Arg.Any<CancellationToken>()).Returns(false);

        Assert.IsType<NoContentResult>(await _sut.Notified("good", new AgentConnectionNotifiedRequest(12, true), Key, Ct));
        Assert.IsType<NotFoundResult>(await _sut.Notified("bad", new AgentConnectionNotifiedRequest(12, true), Key, Ct));
    }

    [Fact]
    public async Task Revoke_needs_a_tenant_and_a_user()
    {
        Assert.IsType<BadRequestResult>(await _sut.Revoke("x", new AgentGrantRevokeRequest(0, "55", false), Key, Ct));
        Assert.IsType<BadRequestResult>(await _sut.Revoke("x", new AgentGrantRevokeRequest(12, " ", false), Key, Ct));
    }
}
