using Hub.Features.AgentAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Tests.Features.AgentAccess;

// Phase 1.7: what Integration Manager shows on its Connected AI apps card and does when someone presses Revoke.
public class AgentGrantAdminTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class Rig : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public AgentGrantStore Store { get; }
        public AgentGrantAdmin Admin { get; }

        public Rig()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var name = Guid.NewGuid().ToString("N");
            services.AddDbContext<AgentAccessOpenIddictDbContext>(o =>
            {
                o.UseInMemoryDatabase(name);
                o.UseOpenIddict<Guid>();
            });
            services.AddOpenIddict().AddCore(c => c.UseEntityFrameworkCore().UseDbContext<AgentAccessOpenIddictDbContext>().ReplaceDefaultEntities<Guid>());
            Provider = services.BuildServiceProvider();
            Store = new AgentGrantStore(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
            Admin = new AgentGrantAdmin(Provider.GetRequiredService<IServiceScopeFactory>(), Store);
        }

        public async Task<string> AddApp(string clientId, string displayName)
        {
            using var scope = Provider.CreateScope();
            var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var app = await apps.CreateAsync(new OpenIddictApplicationDescriptor { ClientId = clientId, DisplayName = displayName }, Ct);
            return (await apps.GetIdAsync(app, Ct))!;
        }

        public async Task<string> AddGrant(string appId, string userId, int tenantId, string type = AuthorizationTypes.Permanent, string status = Statuses.Valid, DateTimeOffset? created = null, string? lastUsed = null, string? notified = null)
        {
            using var scope = Provider.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            var descriptor = new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = appId, Subject = userId, Type = type, Status = status, CreationDate = created ?? Created
            };
            descriptor.Scopes.Add("mcp");
            if (notified is not null) descriptor.Properties["notified_utc"] = System.Text.Json.JsonSerializer.SerializeToElement(notified);
            if (lastUsed is not null) descriptor.Properties["last_used_utc"] = System.Text.Json.JsonSerializer.SerializeToElement(lastUsed);
            var grant = await manager.CreateAsync(descriptor, Ct);
            var id = (await manager.GetIdAsync(grant, Ct))!;

            // The in-memory store has no computed columns; SQL Server fills TenantId from Properties.tenant_id.
            var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
            var row = await ctx.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().FindAsync([Guid.Parse(id)], Ct);
            ctx.Entry(row!).Property<int?>("TenantId").CurrentValue = tenantId;
            await ctx.SaveChangesAsync(Ct);
            return id;
        }

        public async Task<string?> PropertyOf(string id, string name)
        {
            using var scope = Provider.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
            var json = (await ctx.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().FindAsync([Guid.Parse(id)], Ct))!.Properties;
            if (json is null) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        }

        public async Task<string?> StatusOf(string id)
        {
            using var scope = Provider.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
            return (await ctx.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().FindAsync([Guid.Parse(id)], Ct))!.Status;
        }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    [Fact]
    public async Task A_user_sees_only_their_own_live_grants_on_this_tenant()
    {
        await using var rig = new Rig();
        var claude = await rig.AddApp("dfrnt-claude", "Claude");
        var mine = await rig.AddGrant(claude, "55", 12);
        await rig.AddGrant(claude, "56", 12);
        await rig.AddGrant(claude, "55", 99);
        await rig.AddGrant(claude, "55", 12, status: Statuses.Revoked);
        await rig.AddGrant(claude, "55", 12, type: AuthorizationTypes.AdHoc);

        var grants = await rig.Admin.ListAsync(tenantId: 12, userId: "55", allUsers: false, Ct);

        var grant = Assert.Single(grants);
        Assert.Equal(mine, grant.Id);
        Assert.Equal("Claude", grant.ClientName);
        Assert.Equal("55", grant.UserId);
        Assert.Equal(Created, grant.CreatedUtc);
    }

    [Fact]
    public async Task The_list_says_when_each_grant_was_last_used()
    {
        await using var rig = new Rig();
        var claude = await rig.AddApp("dfrnt-claude", "Claude");
        await rig.AddGrant(claude, "55", 12, lastUsed: "2026-10-14T01:02:03.0000000+00:00");
        await rig.AddGrant(claude, "55", 12, created: Created.AddDays(1));

        var grants = await rig.Admin.ListAsync(12, "55", false, Ct);

        Assert.Null(grants[0].LastUsedUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-10-14T01:02:03+00:00"), grants[1].LastUsedUtc);
    }

    [Fact]
    public async Task New_connections_are_the_live_grants_on_the_tenant_nobody_has_been_told_about_oldest_first()
    {
        await using var rig = new Rig();
        var claude = await rig.AddApp("dfrnt-claude", "Claude");
        var newer = await rig.AddGrant(claude, "56", 12, created: Created.AddDays(2));
        var older = await rig.AddGrant(claude, "55", 12, created: Created);
        await rig.AddGrant(claude, "57", 12, notified: "2026-10-14T00:00:00+00:00");
        await rig.AddGrant(claude, "58", 99);
        await rig.AddGrant(claude, "59", 12, status: Statuses.Revoked);

        var connections = await rig.Admin.ListUnnotifiedAsync(12, Ct);

        Assert.Equal([older, newer], connections.Select(c => c.Id).ToArray());
        Assert.Equal(["55", "56"], connections.Select(c => c.UserId).ToArray());
        Assert.All(connections, c => Assert.Equal("Claude", c.ClientName));
    }

    [Fact]
    public async Task At_most_fifty_new_connections_are_returned_at_a_time()
    {
        await using var rig = new Rig();
        var claude = await rig.AddApp("dfrnt-claude", "Claude");
        for (var i = 0; i < 55; i++) await rig.AddGrant(claude, i.ToString(), 12, created: Created.AddMinutes(i));

        Assert.Equal(50, (await rig.Admin.ListUnnotifiedAsync(12, Ct)).Count);
    }

    [Theory]
    [InlineData(true, "sent")]
    [InlineData(false, "held")]
    public async Task Marking_a_connection_notified_records_when_and_whether_it_was_sent(bool sent, string outcome)
    {
        await using var rig = new Rig();
        var id = await rig.AddGrant(await rig.AddApp("dfrnt-claude", "Claude"), "55", 12);

        Assert.True(await rig.Admin.MarkNotifiedAsync(id, 12, sent, Ct));

        Assert.Empty(await rig.Admin.ListUnnotifiedAsync(12, Ct));
        Assert.Equal(outcome, await rig.PropertyOf(id, "notification"));
        Assert.False(string.IsNullOrEmpty(await rig.PropertyOf(id, "notified_utc")));
        Assert.Equal(Statuses.Valid, await rig.StatusOf(id));
    }

    [Fact]
    public async Task Marking_twice_is_fine_and_keeps_the_first_outcome()
    {
        await using var rig = new Rig();
        var id = await rig.AddGrant(await rig.AddApp("dfrnt-claude", "Claude"), "55", 12);

        await rig.Admin.MarkNotifiedAsync(id, 12, sent: true, Ct);
        Assert.True(await rig.Admin.MarkNotifiedAsync(id, 12, sent: false, Ct));

        Assert.Equal("sent", await rig.PropertyOf(id, "notification"));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Marking_an_unknown_connection_is_not_found(string id)
    {
        await using var rig = new Rig();

        Assert.False(await rig.Admin.MarkNotifiedAsync(id, 12, true, Ct));
    }

    [Fact]
    public async Task A_connection_on_another_tenant_or_a_revoked_one_cannot_be_marked()
    {
        await using var rig = new Rig();
        var claude = await rig.AddApp("dfrnt-claude", "Claude");
        var other = await rig.AddGrant(claude, "55", 99);
        var revoked = await rig.AddGrant(claude, "55", 12, status: Statuses.Revoked);

        Assert.False(await rig.Admin.MarkNotifiedAsync(other, 12, true, Ct));
        Assert.False(await rig.Admin.MarkNotifiedAsync(revoked, 12, true, Ct));
    }

    [Fact]
    public async Task A_df_admin_can_list_every_live_grant_on_the_tenant_newest_first()
    {
        await using var rig = new Rig();
        var claude = await rig.AddApp("dfrnt-claude", "Claude");
        var older = await rig.AddGrant(claude, "55", 12, created: Created);
        var newer = await rig.AddGrant(claude, "56", 12, created: Created.AddDays(5));
        await rig.AddGrant(claude, "57", 99);

        var grants = await rig.Admin.ListAsync(12, "1", allUsers: true, Ct);

        Assert.Equal([newer, older], grants.Select(g => g.Id).ToArray());
    }

    [Fact]
    public async Task Revoking_your_own_grant_revokes_it_and_removes_the_liveness_key()
    {
        await using var rig = new Rig();
        var id = await rig.AddGrant(await rig.AddApp("dfrnt-claude", "Claude"), "55", 12);
        await rig.Store.MarkLiveAsync(id, Ct);

        var result = await rig.Admin.RevokeAsync(id, 12, "55", allUsers: false, Ct);

        Assert.Equal(AgentGrantRevokeResult.Revoked, result);
        Assert.Equal(Statuses.Revoked, await rig.StatusOf(id));
        Assert.False(await rig.Store.IsLiveAsync(id, Ct));
    }

    [Fact]
    public async Task Nobody_can_revoke_someone_elses_grant_unless_allowed_to_act_for_everyone()
    {
        await using var rig = new Rig();
        var id = await rig.AddGrant(await rig.AddApp("dfrnt-claude", "Claude"), "56", 12);

        Assert.Equal(AgentGrantRevokeResult.NotFound, await rig.Admin.RevokeAsync(id, 12, "55", allUsers: false, Ct));
        Assert.Equal(Statuses.Valid, await rig.StatusOf(id));

        Assert.Equal(AgentGrantRevokeResult.Revoked, await rig.Admin.RevokeAsync(id, 12, "1", allUsers: true, Ct));
        Assert.Equal(Statuses.Revoked, await rig.StatusOf(id));
    }

    [Fact]
    public async Task A_grant_on_another_tenant_cannot_be_revoked_even_by_a_df_admin_acting_here()
    {
        await using var rig = new Rig();
        var id = await rig.AddGrant(await rig.AddApp("dfrnt-claude", "Claude"), "55", 99);

        Assert.Equal(AgentGrantRevokeResult.NotFound, await rig.Admin.RevokeAsync(id, 12, "55", allUsers: true, Ct));
        Assert.Equal(Statuses.Valid, await rig.StatusOf(id));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Unknown_or_malformed_ids_are_not_found(string id)
    {
        await using var rig = new Rig();

        Assert.Equal(AgentGrantRevokeResult.NotFound, await rig.Admin.RevokeAsync(id, 12, "55", allUsers: true, Ct));
    }

    [Fact]
    public async Task Revoking_twice_is_not_an_error()
    {
        await using var rig = new Rig();
        var id = await rig.AddGrant(await rig.AddApp("dfrnt-claude", "Claude"), "55", 12);

        await rig.Admin.RevokeAsync(id, 12, "55", false, Ct);

        Assert.Equal(AgentGrantRevokeResult.NotFound, await rig.Admin.RevokeAsync(id, 12, "55", false, Ct));
    }
}
