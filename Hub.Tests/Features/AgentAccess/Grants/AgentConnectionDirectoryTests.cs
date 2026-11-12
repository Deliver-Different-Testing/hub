using Hub.Features.AgentAccess.Capabilities;
using Hub.Features.AgentAccess.Grants;
using Hub.Interfaces;
using Hub.Models;
using Hub.Models.Master;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Grants;

// The admin list: one row per live grant on the tenant, with who holds it and what it can do now.
[Collection("PartnerDirectoryApiKey")]
public class AgentConnectionDirectoryTests : IDisposable
{
    private readonly string? _original = Environment.GetEnvironmentVariable("SQLCredentials");
    private readonly IAgentGrantAdmin _admin = Substitute.For<IAgentGrantAdmin>();
    private readonly IAuthenticationRepository _master = Substitute.For<IAuthenticationRepository>();
    private readonly IDespatchRepository _tenant = Substitute.For<IDespatchRepository>();
    private readonly IConnectionStringManager _connection = Substitute.For<IConnectionStringManager>();
    private readonly IAgentCapabilityResolver _capabilities = Substitute.For<IAgentCapabilityResolver>();
    private readonly IAgentPassIdentityLoader _loader = Substitute.For<IAgentPassIdentityLoader>();

    public AgentConnectionDirectoryTests()
    {
        Environment.SetEnvironmentVariable("SQLCredentials", ";User Id=x");
        _master.GetTenantConnectionStringAsync(12).Returns("Server=s");
        _master.GetTenantByIdAsync(12).Returns(new Tenant { CountryCode = "NZ" });
        _loader.LoadAsync(12, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AgentPassLoad(AgentGrantEligibility.Eligible, null, new HashSet<string>(), new Dictionary<string, AgentToolDecision>()));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SQLCredentials", _original);
        GC.SuppressFinalize(this);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AgentConnectionDirectory Sut() => new(_admin, _master, _tenant, _connection, _capabilities, _loader);

    [Fact]
    public async Task Every_live_grant_on_the_tenant_is_listed_with_its_holder_roles_and_capabilities()
    {
        var created = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var used = created.AddDays(3);
        _admin.ListNewestAsync(12, 501, Arg.Any<CancellationToken>())
            .Returns([new AgentGrantSummary("g1", "55", "Claude", created, used, 9)]);
        _master.GetUserByIdAsync(55).Returns(new User { UserId = 55, Email = "sam@urgent.test" });
        _tenant.FetchAgentContactAsync(9).Returns(new TucClientContact
        {
            UcctId = 9, UcctClientId = 3, UcctFirstname = "Sam", UcctSurname = "Staff",
            UcctClient = new TucClient { UcclId = 3, UcclInternal = true }
        });
        _capabilities.RoleNamesAsync(9, Arg.Any<CancellationToken>()).Returns(["Dispatcher", "Admin"]);
        _loader.LoadAsync(12, "55", 9, Arg.Any<CancellationToken>()).Returns(new AgentPassLoad(
            AgentGrantEligibility.Eligible, null, new HashSet<string> { AgentCapabilities.SeeCosts, AgentCapabilities.Connect }));

        var connections = await Sut().ListAsync(12, Ct);

        var view = Assert.Single(connections!.Connections);
        Assert.Equal(
            new AgentConnectionView("g1", "55", 9, "Sam Staff", "sam@urgent.test", ["Dispatcher", "Admin"], "Claude", created, used,
                [AgentCapabilities.Connect, AgentCapabilities.SeeCosts]),
            view with { Roles = view.Roles.ToList(), Capabilities = view.Capabilities.ToList() },
            new ViewComparer());
    }

    [Fact]
    public async Task A_grant_whose_contact_is_gone_is_still_listed_so_it_can_be_revoked()
    {
        _admin.ListNewestAsync(12, 501, Arg.Any<CancellationToken>())
            .Returns([new AgentGrantSummary("g1", "55", "Claude", null, null, 9)]);
        _master.GetUserByIdAsync(55).Returns(new User { UserId = 55, Email = "sam@urgent.test" });

        var view = Assert.Single((await Sut().ListAsync(12, Ct))!.Connections);

        Assert.Equal("sam@urgent.test", view.UserName);
        Assert.Empty(view.Roles);
        Assert.Empty(view.Capabilities);
        Assert.Null(view.LastUsedAt);
    }

    [Fact]
    public async Task No_grants_means_an_empty_list_without_touching_the_tenant_database()
    {
        _admin.ListNewestAsync(12, 501, Arg.Any<CancellationToken>()).Returns([]);

        Assert.Empty((await Sut().ListAsync(12, Ct))!.Connections);
        _connection.DidNotReceiveWithAnyArgs().SetConnectionString(null!);
    }

    [Fact]
    public async Task A_tenant_with_no_connection_is_unavailable_not_empty()
    {
        _admin.ListNewestAsync(12, 501, Arg.Any<CancellationToken>())
            .Returns([new AgentGrantSummary("g1", "55", "Claude", null)]);
        _master.GetTenantConnectionStringAsync(12).Returns((string?)null);

        Assert.Null(await Sut().ListAsync(12, Ct));
    }

    [Fact]
    public async Task A_user_with_several_grants_is_looked_up_once()
    {
        _admin.ListNewestAsync(12, 501, Arg.Any<CancellationToken>()).Returns(
        [
            new AgentGrantSummary("g1", "55", "Claude", null, null, 9),
            new AgentGrantSummary("g2", "55", "ChatGPT", null, null, 9)
        ]);
        _master.GetUserByIdAsync(55).Returns(new User { UserId = 55, Email = "sam@urgent.test" });
        _tenant.FetchAgentContactAsync(9).Returns(new TucClientContact
        {
            UcctId = 9, UcctClientId = 3, UcctFirstname = "Sam", UcctSurname = "Staff",
            UcctClient = new TucClient { UcclId = 3, UcclInternal = true }
        });
        _capabilities.RoleNamesAsync(9, Arg.Any<CancellationToken>()).Returns(["Dispatcher"]);

        var result = await Sut().ListAsync(12, Ct);

        Assert.Equal(2, result!.Connections.Count);
        await _master.Received(1).GetUserByIdAsync(55);
        await _tenant.Received(1).FetchAgentContactAsync(9);
        await _capabilities.Received(1).RoleNamesAsync(9, Arg.Any<CancellationToken>());
        await _loader.Received(1).LoadAsync(12, "55", 9, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task More_than_the_cap_lists_the_newest_and_says_it_was_truncated()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // Newest first, one more than the cap, as the query returns it.
        var grants = Enumerable.Range(0, 501)
            .Select(i => new AgentGrantSummary($"g{500 - i}", "55", "Claude", start.AddMinutes(500 - i)))
            .ToList();
        _admin.ListNewestAsync(12, 501, Arg.Any<CancellationToken>()).Returns(grants);

        var result = await Sut().ListAsync(12, Ct);

        Assert.True(result!.Truncated);
        Assert.Equal(500, result.Connections.Count);
        Assert.Equal("g500", result.Connections[0].GrantId);
        Assert.DoesNotContain(result.Connections, c => c.GrantId == "g0");
    }

    [Fact]
    public async Task At_the_cap_is_not_truncated()
    {
        _admin.ListNewestAsync(12, 501, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 500).Select(i => new AgentGrantSummary($"g{i}", "x", "Claude", null)).ToList());

        Assert.False((await Sut().ListAsync(12, Ct))!.Truncated);
    }

    [Fact]
    public async Task One_grants_failure_degrades_that_row_only()
    {
        _admin.ListNewestAsync(12, 501, Arg.Any<CancellationToken>()).Returns(
        [
            new AgentGrantSummary("g1", "55", "Claude", null, null, 9),
            new AgentGrantSummary("g2", "56", "ChatGPT", null)
        ]);
        _master.GetUserByIdAsync(55).Returns<User?>(_ => throw new InvalidOperationException("boom"));
        _master.GetUserByIdAsync(56).Returns(new User { UserId = 56, Email = "pat@urgent.test" });

        var views = (await Sut().ListAsync(12, Ct))!.Connections;

        Assert.Equal(2, views.Count);
        Assert.Equal("could not be read", views[0].Error);
        Assert.Empty(views[0].Roles);
        Assert.Empty(views[0].Capabilities);
        Assert.Equal("g1", views[0].GrantId);
        Assert.Null(views[1].Error);
        Assert.Equal("pat@urgent.test", views[1].UserName);
    }

    private sealed class ViewComparer : IEqualityComparer<AgentConnectionView>
    {
        public bool Equals(AgentConnectionView? x, AgentConnectionView? y) =>
            x is not null && y is not null
            && x.GrantId == y.GrantId && x.UserId == y.UserId && x.ContactId == y.ContactId && x.UserName == y.UserName
            && x.Email == y.Email && x.Client == y.Client && x.ConnectedAt == y.ConnectedAt && x.LastUsedAt == y.LastUsedAt
            && x.Roles.SequenceEqual(y.Roles) && x.Capabilities.SequenceEqual(y.Capabilities);

        public int GetHashCode(AgentConnectionView obj) => obj.GrantId.GetHashCode();
    }

    [Theory]
    [InlineData(AgentGrantEligibility.NoLongerEligible)]
    [InlineData(AgentGrantEligibility.TenantOff)]
    [InlineData(AgentGrantEligibility.NotPermitted)]
    public async Task A_holder_who_would_be_refused_shows_no_capabilities_and_why(AgentGrantEligibility eligibility)
    {
        _admin.ListNewestAsync(12, 501, Arg.Any<CancellationToken>()).Returns([new AgentGrantSummary("g1", "55", "Claude", null, null, 9)]);
        _master.GetUserByIdAsync(55).Returns(new User { UserId = 55, Email = "sam@urgent.test" });
        _tenant.FetchAgentContactAsync(9).Returns(new TucClientContact { UcctId = 9, UcctClientId = 3, UcctFirstname = "Sam", UcctSurname = "Staff" });
        _loader.LoadAsync(12, "55", 9, Arg.Any<CancellationToken>())
            .Returns(new AgentPassLoad(eligibility, null, new HashSet<string> { AgentCapabilities.Connect }));

        var view = Assert.Single((await Sut().ListAsync(12, Ct))!.Connections);

        Assert.Empty(view.Capabilities);
        Assert.Equal(eligibility.ToString(), view.Eligibility);
    }

    [Fact]
    public async Task A_grant_with_no_contact_id_is_no_longer_eligible_without_asking_the_loader()
    {
        _admin.ListNewestAsync(12, 501, Arg.Any<CancellationToken>()).Returns([new AgentGrantSummary("g1", "55", "Claude", null)]);

        var view = Assert.Single((await Sut().ListAsync(12, Ct))!.Connections);

        Assert.Equal("NoLongerEligible", view.Eligibility);
        await _loader.DidNotReceiveWithAnyArgs().LoadAsync(0, null!, 0, Ct);
    }

    [Fact]
    public async Task A_contact_with_a_blank_name_falls_back_to_the_users_email()
    {
        _admin.ListNewestAsync(12, 501, Arg.Any<CancellationToken>()).Returns([new AgentGrantSummary("g1", "55", "Claude", null, null, 9)]);
        _master.GetUserByIdAsync(55).Returns(new User { UserId = 55, Email = "sam@urgent.test" });
        _tenant.FetchAgentContactAsync(9).Returns(new TucClientContact { UcctId = 9, UcctFirstname = " ", UcctSurname = "" });

        Assert.Equal("sam@urgent.test", Assert.Single((await Sut().ListAsync(12, Ct))!.Connections).UserName);
    }
}
