using Hub.Features.Outbox;
using Hub.Interfaces;
using Hub.Models.Master;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Hub.Tests.Features.Outbox;

public class OutboxResetRegeneratorTests
{
    private const string OldKey = "11111111-2222-3333-4444-555555555555";
    private const string TenantUrl = "https://app_name.urgent.deliverdifferent.com";

    private readonly IAuthenticationRepository _master = Substitute.For<IAuthenticationRepository>();
    private readonly IOutboxRegenerationSource _source = Substitute.For<IOutboxRegenerationSource>();
    private readonly Tenant _tenant = new() { TenantId = 5, Code = "urgent", Dbconnection = "Server=x;Database=Despatch-Urgent;" };
    private readonly User _user = new() { UserId = 42, Email = "pat@example.com", ResetKey = OldKey, CurrentTenantId = 5 };

    public OutboxResetRegeneratorTests()
    {
        _master.GetTenantByCodeAsync("urgent").Returns(_tenant);
        _master.GetUserByEmailAsync("pat@example.com").Returns(_user);
        _source.GetClaimedRowAsync(Arg.Any<string>(), 900, Arg.Any<CancellationToken>())
            .Returns(Row());
        _source.GetContactAsync(Arg.Any<string>(), 77, Arg.Any<CancellationToken>())
            .Returns(new OutboxContact("pat@example.com", true));
    }

    private static OutboxRegenerationRow Row(string type = "PasswordReset", int? contactId = 77, string? reference = "77",
        string? body = null) =>
        new(900, type, reference, contactId, "Reset your password",
            body ?? $"<p>Hi Pat</p><a href=\"https://hub.urgent.deliverdifferent.com/Account/ResetPassword?code={OldKey}\">Reset</a>",
            true);

    private OutboxResetRegenerator Create(string? tenantUrl = TenantUrl, string? credentials = "User Id=u;Password=p;") =>
        new(_master, _source, () => tenantUrl, () => credentials);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rebuilds_with_a_fresh_key_that_replaces_the_held_one()
    {
        var result = await Create().RegenerateAsync(900, Ct);

        Assert.Equal(OutboxRegenerationStatus.Rebuilt, result.Status);
        Assert.NotEqual(OldKey, _user.ResetKey);
        Assert.True(Guid.TryParse(_user.ResetKey, out _));
        Assert.Contains($"?code={_user.ResetKey}", result.Body);
        Assert.DoesNotContain(OldKey, result.Body);
        Assert.StartsWith("<p>Hi Pat</p>", result.Body);   // the rest is what was held
        Assert.Equal("Reset your password", result.Subject);
        Assert.True(result.IsHtml);
        await _master.Received(1).SaveAsync();
    }

    [Fact]
    public async Task Reads_the_tenant_database_this_hub_serves_with_its_credentials()
    {
        await Create().RegenerateAsync(900, Ct);

        await _source.Received(1).GetClaimedRowAsync("Server=x;Database=Despatch-Urgent;User Id=u;Password=p;", 900, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Hub_invite_is_rebuilt_too_and_contact_falls_back_to_the_reference()
    {
        _source.GetClaimedRowAsync(Arg.Any<string>(), 900, Arg.Any<CancellationToken>()).Returns(Row("HubInvite", contactId: null));

        var result = await Create().RegenerateAsync(900, Ct);

        Assert.Equal(OutboxRegenerationStatus.Rebuilt, result.Status);
    }

    [Fact]
    public async Task No_claimed_row_is_not_found_and_nothing_is_rotated()
    {
        var result = await Create().RegenerateAsync(901, Ct);

        Assert.Equal(OutboxRegenerationStatus.NotFound, result.Status);
        Assert.Equal(OldKey, _user.ResetKey);
        await _master.DidNotReceive().SaveAsync();
    }

    [Fact]
    public async Task Other_message_types_are_refused()
    {
        _source.GetClaimedRowAsync(Arg.Any<string>(), 900, Arg.Any<CancellationToken>()).Returns(Row("Tracking"));

        var result = await Create().RegenerateAsync(900, Ct);

        Assert.Equal(OutboxRegenerationStatus.Gone, result.Status);
        Assert.Equal(OldKey, _user.ResetKey);
    }

    [Fact]
    public async Task Deleted_or_inactive_contact_is_gone()
    {
        _source.GetContactAsync(Arg.Any<string>(), 77, Arg.Any<CancellationToken>()).Returns(new OutboxContact("pat@example.com", false));
        var inactive = await Create().RegenerateAsync(900, Ct);

        _source.GetContactAsync(Arg.Any<string>(), 77, Arg.Any<CancellationToken>()).Returns((OutboxContact?)null);
        var deleted = await Create().RegenerateAsync(900, Ct);

        Assert.Equal(OutboxRegenerationStatus.Gone, inactive.Status);
        Assert.Equal("Contact no longer exists or is inactive", deleted.Reason);
        Assert.Equal(OldKey, _user.ResetKey);
    }

    [Fact]
    public async Task No_contact_reference_is_gone()
    {
        _source.GetClaimedRowAsync(Arg.Any<string>(), 900, Arg.Any<CancellationToken>()).Returns(Row(contactId: null, reference: null));

        Assert.Equal(OutboxRegenerationStatus.Gone, (await Create().RegenerateAsync(900, Ct)).Status);
    }

    [Fact]
    public async Task Body_without_a_reset_link_is_gone_and_nothing_is_rotated()
    {
        _source.GetClaimedRowAsync(Arg.Any<string>(), 900, Arg.Any<CancellationToken>()).Returns(Row(body: "<p>No link here</p>"));

        var result = await Create().RegenerateAsync(900, Ct);

        Assert.Equal(OutboxRegenerationStatus.Gone, result.Status);
        Assert.Equal(OldKey, _user.ResetKey);
        await _master.DidNotReceive().SaveAsync();
    }

    [Fact]
    public async Task Contact_without_a_hub_login_is_gone()
    {
        _master.GetUserByEmailAsync("pat@example.com").Returns((User?)null);

        Assert.Equal("The contact no longer has a Hub login", (await Create().RegenerateAsync(900, Ct)).Reason);
    }

    [Fact]
    public async Task Login_on_another_tenant_is_refused()
    {
        _user.CurrentTenantId = 8;
        _master.IsUserAssociatedWithTenantAsync(42, 5).Returns(false);

        var result = await Create().RegenerateAsync(900, Ct);

        Assert.Equal(OutboxRegenerationStatus.Gone, result.Status);
        Assert.Equal(OldKey, _user.ResetKey);
    }

    [Fact]
    public async Task Login_associated_with_this_tenant_but_currently_elsewhere_is_allowed()
    {
        _user.CurrentTenantId = 8;
        _master.IsUserAssociatedWithTenantAsync(42, 5).Returns(true);

        Assert.Equal(OutboxRegenerationStatus.Rebuilt, (await Create().RegenerateAsync(900, Ct)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://localhost:5001")]
    public async Task Unresolvable_tenant_is_unavailable_and_reads_nothing(string? url)
    {
        var result = await Create(tenantUrl: url).RegenerateAsync(900, Ct);

        Assert.Equal(OutboxRegenerationStatus.Unavailable, result.Status);
        await _source.DidNotReceiveWithAnyArgs().GetClaimedRowAsync(null!, 0, Ct);
    }

    [Fact]
    public async Task Missing_credentials_is_unavailable()
    {
        Assert.Equal(OutboxRegenerationStatus.Unavailable, (await Create(credentials: null).RegenerateAsync(900, Ct)).Status);
    }

    [Theory]
    [InlineData("https://app_name.urgent.deliverdifferent.com", "urgent")]
    [InlineData("https://app_name.medical.staging.deliverdifferent.com", "medical")]
    [InlineData("https://app_name.staging.deliverdifferent.com", "dfrnt")]
    [InlineData("https://app_name.local.deliverdifferent.com", null)]
    [InlineData("not a url", null)]
    public void Tenant_code_comes_from_TenantURL(string url, string? expected) =>
        Assert.Equal(expected, OutboxResetRegenerator.TenantCodeFrom(url));

    [Fact]
    public void Every_code_in_the_body_is_replaced_including_html_escaped_ones()
    {
        var body = $"a?code={OldKey} b&code={OldKey} c&amp;code={OldKey} d?other={OldKey}";

        var result = OutboxResetRegenerator.ReplaceCode(body, "NEW");

        Assert.Equal($"a?code=NEW b&code=NEW c&amp;code=NEW d?other={OldKey}", result);
    }
}

public class OutboxRegenerateApiControllerTests : IDisposable
{
    private readonly IOutboxResetRegenerator _regenerator = Substitute.For<IOutboxResetRegenerator>();
    private readonly string? _previous = Environment.GetEnvironmentVariable("OutboxInternalApiKey");

    public OutboxRegenerateApiControllerTests() => Environment.SetEnvironmentVariable("OutboxInternalApiKey", "secret-key");

    public void Dispose() => Environment.SetEnvironmentVariable("OutboxInternalApiKey", _previous);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task Bad_key_is_unauthorized_and_does_nothing(string? key)
    {
        var result = await new OutboxRegenerateApiController(_regenerator).Regenerate(900, key, Ct);

        Assert.IsType<UnauthorizedResult>(result);
        await _regenerator.DidNotReceiveWithAnyArgs().RegenerateAsync(0, Ct);
    }

    [Fact]
    public async Task Unset_key_closes_the_endpoint()
    {
        Environment.SetEnvironmentVariable("OutboxInternalApiKey", null);

        Assert.IsType<UnauthorizedResult>(await new OutboxRegenerateApiController(_regenerator).Regenerate(900, "", Ct));
    }

    [Fact]
    public async Task Outcomes_map_to_status_codes()
    {
        var controller = new OutboxRegenerateApiController(_regenerator);

        _regenerator.RegenerateAsync(1, Arg.Any<CancellationToken>())
            .Returns(new OutboxRegenerationResult(OutboxRegenerationStatus.Rebuilt, "S", "B", true));
        var ok = Assert.IsType<OkObjectResult>(await controller.Regenerate(1, "secret-key", Ct));
        Assert.Equal(new OutboxRegeneratedMessage("S", "B", true), ok.Value);

        _regenerator.RegenerateAsync(2, Arg.Any<CancellationToken>()).Returns(OutboxRegenerationResult.NotFound);
        Assert.IsType<NotFoundResult>(await controller.Regenerate(2, "secret-key", Ct));

        _regenerator.RegenerateAsync(3, Arg.Any<CancellationToken>()).Returns(OutboxRegenerationResult.Gone("gone"));
        var gone = Assert.IsType<ObjectResult>(await controller.Regenerate(3, "secret-key", Ct));
        Assert.Equal(410, gone.StatusCode);
        Assert.Equal(new OutboxRegenerationRefusal("gone"), gone.Value);

        _regenerator.RegenerateAsync(4, Arg.Any<CancellationToken>()).Returns(OutboxRegenerationResult.Unavailable("no tenant"));
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.Regenerate(4, "secret-key", Ct)).StatusCode);

        Assert.IsType<BadRequestResult>(await controller.Regenerate(0, "secret-key", Ct));
    }
}
