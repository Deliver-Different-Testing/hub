using System.Security.Cryptography;
using Hub.Features.AgentAccess.Grants;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Hub.Features.AgentAccess.Oauth;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Tests.Features.AgentAccess.Grants;

// The admin Connected AI apps list and Revoke (configurator proxies them). Service-account bearer token, tenant scoped, audited.
[Collection("PartnerDirectoryApiKey")]
public class AgentAccessConnectionsApiControllerTests : IDisposable
{
    private const string Cluster = "https://oidc.eks.ap-southeast-2.amazonaws.com/id/ABC";
    private const string Subject = "system:serviceaccount:dfrntdrive-configurator-804-urgent-staging:dfrntdrive-configurator";
    private const string AllowedPrefix = "system:serviceaccount:dfrntdrive-configurator-804-*";
    private const string SharedKey = "shared-key";
    private static readonly RsaSecurityKey ClusterKey = new(RSA.Create(2048)) { KeyId = "k1" };
    private readonly string? _originalShared = Environment.GetEnvironmentVariable("PartnerDirectoryApiKey");
    private readonly IAgentConnectionDirectory _directory = Substitute.For<IAgentConnectionDirectory>();
    private readonly IAgentAdminAudit _audit = Substitute.For<IAgentAdminAudit>();
    private readonly string Key = Bearer(Mint());

    public AgentAccessConnectionsApiControllerTests()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", SharedKey);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PartnerDirectoryApiKey", _originalShared);
        GC.SuppressFinalize(this);
    }

    private static string Bearer(string token) => "Bearer " + token;

    private static string Mint(
        string subject = Subject, string audience = AgentAccessAdminTokenValidator.Audience, string issuer = Cluster,
        TimeSpan? expiresIn = null, SecurityKey? key = null)
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(expiresIn ?? TimeSpan.FromMinutes(10));
        var start = expires < now ? now.AddHours(-1) : now;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = new Dictionary<string, object> { ["sub"] = subject },
            IssuedAt = start,
            NotBefore = start,
            Expires = expires,
            SigningCredentials = new SigningCredentials(key ?? ClusterKey, SecurityAlgorithms.RsaSha256)
        });
    }

    private sealed class StaticJwks : IAgentAccessClusterJwks
    {
        public BaseConfigurationManager? Manager { get; } = new StaticConfigurationManager<OpenIdConnectConfiguration>(
            new OpenIdConnectConfiguration { Issuer = Cluster, SigningKeys = { ClusterKey } });
    }

    private static IAgentAccessAdminTokenValidator Tokens(params string[] allowed) => new AgentAccessAdminTokenValidator(
        new AgentAccessAdminTokenOptions(AgentAccessMcpAssertionOptions.Resolve(Cluster, allowed)), new StaticJwks());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly AgentConnectionRevokeRequest Admin = new("77", "Ada Admin");

    // Real grant admin over an in-memory OAuth store and cache, so revoke is proved end to end.
    private sealed class Rig : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public AgentGrantStore Store { get; }
        public AgentGrantAdmin GrantAdmin { get; }
        public IDistributedCache Cache { get; } = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

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
            Store = new AgentGrantStore(Cache);
            GrantAdmin = new AgentGrantAdmin(Provider.GetRequiredService<IServiceScopeFactory>(), Store);
        }

        public async Task<string> AddGrant(string userId, int tenantId)
        {
            using var scope = Provider.CreateScope();
            var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var app = await apps.CreateAsync(new OpenIddictApplicationDescriptor { ClientId = "dfrnt-claude", DisplayName = "Claude" }, Ct);
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            var grant = await manager.CreateAsync(new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = await apps.GetIdAsync(app, Ct), Subject = userId, Type = AuthorizationTypes.Permanent,
                Status = Statuses.Valid, CreationDate = DateTimeOffset.UtcNow
            }, Ct);
            var id = (await manager.GetIdAsync(grant, Ct))!;

            // The in-memory store has no computed columns; SQL Server fills TenantId from Properties.tenant_id.
            var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
            var row = await ctx.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().FindAsync([Guid.Parse(id)], Ct);
            ctx.Entry(row!).Property<int?>("TenantId").CurrentValue = tenantId;
            await ctx.SaveChangesAsync(Ct);
            await Store.MarkLiveAsync(id, Ct);
            return id;
        }

        public async Task<string?> StatusOf(string id)
        {
            using var scope = Provider.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
            return (await ctx.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().FindAsync([Guid.Parse(id)], Ct))!.Status;
        }

        public async Task<bool> IsLive(string id) => await Cache.GetAsync("agent-grant:" + id, Ct) is not null;

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private AgentAccessConnectionsApiController Controller(IAgentGrantAdmin admin, params string[] allowed) =>
        new(_directory, admin, _audit, Tokens(allowed is { Length: > 0 } ? allowed : [AllowedPrefix]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("Bearer ")]
    [InlineData("Bearer not.a.jwt")]
    [InlineData("Basic abc")]
    public async Task A_missing_or_malformed_token_is_refused_and_nothing_is_read_or_changed(string? key)
    {
        var admin = Substitute.For<IAgentGrantAdmin>();
        var sut = Controller(admin);

        Assert.IsType<UnauthorizedResult>(await sut.List(12, key, Ct));
        Assert.IsType<UnauthorizedResult>(await sut.Revoke("g1", 12, Admin, key, Ct));

        await _directory.DidNotReceiveWithAnyArgs().ListAsync(0, Ct);
        await admin.DidNotReceiveWithAnyArgs().RevokeAsync(null!, 0, null!, false, Ct);
        _audit.DidNotReceiveWithAnyArgs().GrantRevoked(0, null!, null!, null!, null!);
    }

    [Fact]
    public async Task List_returns_the_tenants_connections()
    {
        var view = new AgentConnectionView(
            "g1", "55", 9, "Sam Staff", "sam@urgent.test", ["Dispatcher"], "Claude", DateTimeOffset.UnixEpoch, null, ["agent-connect"]);
        _directory.ListAsync(12, Arg.Any<CancellationToken>()).Returns(new AgentConnectionList([view], false));

        var result = await Controller(Substitute.For<IAgentGrantAdmin>()).List(12, Key, Ct);

        var body = Assert.IsType<AgentConnectionsResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Same(view, Assert.Single(body.Connections));
    }

    [Fact]
    public async Task List_for_a_tenant_that_cannot_be_reached_is_unavailable()
    {
        _directory.ListAsync(12, Arg.Any<CancellationToken>()).Returns((AgentConnectionList?)null);

        var result = await Controller(Substitute.For<IAgentGrantAdmin>()).List(12, Key, Ct);

        Assert.Equal(503, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Fact]
    public async Task List_needs_a_tenant()
    {
        Assert.IsType<BadRequestResult>(await Controller(Substitute.For<IAgentGrantAdmin>()).List(0, Key, Ct));
    }

    [Theory]
    [InlineData(0, "77")]
    [InlineData(12, "")]
    [InlineData(12, null)]
    public async Task Revoke_needs_a_tenant_and_an_actor(int tenantId, string? actor)
    {
        var admin = Substitute.For<IAgentGrantAdmin>();

        var result = await Controller(admin).Revoke("g1", tenantId, new AgentConnectionRevokeRequest(actor, "Ada"), Key, Ct);

        Assert.IsType<BadRequestResult>(result);
        await admin.DidNotReceiveWithAnyArgs().RevokeAsync(null!, 0, null!, false, Ct);
    }

    [Fact]
    public async Task Revoke_ends_the_grant_immediately_and_audits_the_admin()
    {
        await using var rig = new Rig();
        var grant = await rig.AddGrant("55", 12);
        Assert.True(await rig.IsLive(grant));

        var result = await Controller(rig.GrantAdmin).Revoke(grant, 12, Admin, Key, Ct);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(Statuses.Revoked, await rig.StatusOf(grant));
        Assert.False(await rig.IsLive(grant));
        _audit.Received(1).GrantRevoked(12, grant, "77", "Ada Admin", Subject);
    }

    [Fact]
    public async Task Revoke_of_another_tenants_grant_is_not_found_and_changes_nothing()
    {
        await using var rig = new Rig();
        var elsewhere = await rig.AddGrant("55", 99);

        var result = await Controller(rig.GrantAdmin).Revoke(elsewhere, 12, Admin, Key, Ct);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(Statuses.Valid, await rig.StatusOf(elsewhere));
        Assert.True(await rig.IsLive(elsewhere));
        _audit.DidNotReceiveWithAnyArgs().GrantRevoked(0, null!, null!, null!, null!);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Revoke_of_an_unknown_grant_is_not_found(string id)
    {
        await using var rig = new Rig();

        Assert.IsType<NotFoundResult>(await Controller(rig.GrantAdmin).Revoke(id, 12, Admin, Key, Ct));
        _audit.DidNotReceiveWithAnyArgs().GrantRevoked(0, null!, null!, null!, null!);
    }

    [Fact]
    public async Task A_missing_actor_name_falls_back_to_the_actor_id_in_the_audit()
    {
        await using var rig = new Rig();
        var grant = await rig.AddGrant("55", 12);

        await Controller(rig.GrantAdmin).Revoke(grant, 12, new AgentConnectionRevokeRequest("77", " "), Key, Ct);

        _audit.Received(1).GrantRevoked(12, grant, "77", "77", Subject);
    }

    [Fact]
    public async Task The_shared_partner_directory_key_and_the_old_api_key_header_are_refused_on_these_endpoints()
    {
        var admin = Substitute.For<IAgentGrantAdmin>();
        var sut = Controller(admin);

        Assert.IsType<UnauthorizedResult>(await sut.List(12, SharedKey, Ct));
        Assert.IsType<UnauthorizedResult>(await sut.Revoke("g1", 12, Admin, SharedKey, Ct));
        await admin.DidNotReceiveWithAnyArgs().RevokeAsync(null!, 0, null!, false, Ct);
    }

    [Fact]
    public async Task A_valid_service_account_token_is_accepted()
    {
        _directory.ListAsync(12, Arg.Any<CancellationToken>()).Returns(new AgentConnectionList([], true));

        var ok = Assert.IsType<OkObjectResult>(await Controller(Substitute.For<IAgentGrantAdmin>()).List(12, Key, Ct));
        Assert.True(Assert.IsType<AgentConnectionsResponse>(ok.Value).Truncated);
    }

    [Fact]
    public async Task A_missing_body_is_a_bad_request()
    {
        var admin = Substitute.For<IAgentGrantAdmin>();

        Assert.IsType<BadRequestResult>(await Controller(admin).Revoke("g1", 12, null, Key, Ct));
        await admin.DidNotReceiveWithAnyArgs().RevokeAsync(null!, 0, null!, false, Ct);
    }

    [Fact]
    public async Task A_revoke_that_did_not_take_effect_is_unavailable_and_not_audited()
    {
        var admin = Substitute.For<IAgentGrantAdmin>();
        admin.RevokeAsync("g1", 12, string.Empty, true, Arg.Any<CancellationToken>()).Returns(AgentGrantRevokeResult.Failed);

        var result = await Controller(admin).Revoke("g1", 12, Admin, Key, Ct);

        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        _audit.DidNotReceiveWithAnyArgs().GrantRevoked(0, null!, null!, null!, null!);
    }

    [Fact]
    public async Task The_actor_name_is_capped_and_stripped_of_control_characters_in_the_audit()
    {
        var admin = Substitute.For<IAgentGrantAdmin>();
        admin.RevokeAsync("g1", 12, string.Empty, true, Arg.Any<CancellationToken>()).Returns(AgentGrantRevokeResult.Revoked);
        var name = "Ada\r\nFAKE" + new string('x', 300);

        await Controller(admin).Revoke("g1", 12, new AgentConnectionRevokeRequest("77", name), Key, Ct);

        _audit.Received(1).GrantRevoked(12, "g1", "77", Arg.Is<string>(n => n.Length == 100 && n.StartsWith("AdaFAKEx") && !n.Contains('\n')), Subject);
    }

    [Fact]
    public async Task A_redis_failure_after_the_revoke_is_audited_and_reported_as_pending()
    {
        await using var rig = new Rig();
        var grant = await rig.AddGrant("55", 12);
        var failing = Substitute.For<IAgentGrantStore>();
        failing.RevokeAsync(grant, Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("redis down")));
        var admin = new AgentGrantAdmin(rig.Provider.GetRequiredService<IServiceScopeFactory>(), failing);

        var result = await Controller(admin).Revoke(grant, 12, Admin, Key, Ct);

        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Contains("30 minutes", result is ObjectResult { Value: { } v } ? v.ToString() : "");
        Assert.Equal(Statuses.Revoked, await rig.StatusOf(grant));
        _audit.Received(1).GrantRevoked(12, grant, "77", "Ada Admin", Subject);
    }

    [Fact]
    public async Task A_valid_token_from_an_allowed_subject_can_list_and_revoke()
    {
        _directory.ListAsync(12, Arg.Any<CancellationToken>()).Returns(new AgentConnectionList([], false));
        var admin = Substitute.For<IAgentGrantAdmin>();
        admin.RevokeAsync("g1", 12, string.Empty, true, Arg.Any<CancellationToken>()).Returns(AgentGrantRevokeResult.Revoked);
        var sut = Controller(admin);

        Assert.IsType<OkObjectResult>(await sut.List(12, Key, Ct));
        Assert.IsType<NoContentResult>(await sut.Revoke("g1", 12, Admin, Key, Ct));
    }

    public static TheoryData<string> BadTokens() => new()
    {
        Bearer(Mint(audience: "https://hub.example.test/")),
        Bearer(Mint(issuer: "https://oidc.eks.ap-southeast-2.amazonaws.com/id/OTHER")),
        Bearer(Mint(expiresIn: TimeSpan.FromMinutes(-10))),
        Bearer(Mint(key: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k1" })),
        Bearer(Mint(subject: "system:serviceaccount:other-ns:configurator")),
        Bearer(Mint(subject: "system:serviceaccount:dfrnt-automate-mcp-87595416-staging:mcp")),
    };

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task A_wrong_audience_issuer_expiry_signature_or_subject_is_refused(string authorization)
    {
        var admin = Substitute.For<IAgentGrantAdmin>();
        var sut = Controller(admin);

        Assert.IsType<UnauthorizedResult>(await sut.List(12, authorization, Ct));
        Assert.IsType<UnauthorizedResult>(await sut.Revoke("g1", 12, Admin, authorization, Ct));
        await _directory.DidNotReceiveWithAnyArgs().ListAsync(0, Ct);
        await admin.DidNotReceiveWithAnyArgs().RevokeAsync(null!, 0, null!, false, Ct);
        _audit.DidNotReceiveWithAnyArgs().GrantRevoked(0, null!, null!, null!, null!);
    }

    [Fact]
    public async Task An_empty_allow_list_refuses_everything_even_a_well_formed_token()
    {
        var sut = new AgentAccessConnectionsApiController(
            _directory, Substitute.For<IAgentGrantAdmin>(), _audit, Tokens());

        Assert.IsType<UnauthorizedResult>(await sut.List(12, Key, Ct));
        Assert.IsType<UnauthorizedResult>(await sut.Revoke("g1", 12, Admin, Key, Ct));
    }

    [Fact]
    public async Task An_exact_subject_entry_is_accepted()
    {
        _directory.ListAsync(12, Arg.Any<CancellationToken>()).Returns(new AgentConnectionList([], false));

        Assert.IsType<OkObjectResult>(await Controller(Substitute.For<IAgentGrantAdmin>(), Subject).List(12, Key, Ct));
    }

    [Fact]
    public async Task The_audit_caller_is_the_token_subject()
    {
        var admin = Substitute.For<IAgentGrantAdmin>();
        admin.RevokeAsync("g1", 12, string.Empty, true, Arg.Any<CancellationToken>()).Returns(AgentGrantRevokeResult.Revoked);

        await Controller(admin).Revoke("g1", 12, Admin, Key, Ct);

        _audit.Received(1).GrantRevoked(12, "g1", "77", "Ada Admin", Subject);
    }
}
