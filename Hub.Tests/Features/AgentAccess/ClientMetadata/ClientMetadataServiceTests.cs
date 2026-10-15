using Hub.Features.AgentAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Tests.Features.AgentAccess;

// Phase 1.8: a client ID metadata document URL becomes a cached application row, safely.
public class ClientMetadataServiceTests
{
    private const string Url = "https://claude.ai/oauth/mcp-client";
    private const string Callback = "https://claude.ai/api/mcp/auth_callback";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class Rig : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public IClientMetadataFetcher Fetcher { get; } = Substitute.For<IClientMetadataFetcher>();
        public Clock Time { get; } = new(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero));
        public ClientMetadataFailures Failures { get; } = new();
        public ClientMetadataOptions Options { get; }

        public Rig(params string[] allowedHosts)
        {
            Options = ClientMetadataOptions.Create(allowedHosts.Length == 0 ? ["claude.ai"] : allowedHosts);
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
            Respond(Doc());
        }

        public void Respond(string body) =>
            Fetcher.FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(new ClientMetadataFetchResult(body, null));

        public void Fail(string error = "HTTP 500") =>
            Fetcher.FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(new ClientMetadataFetchResult(null, error));

        public async Task<ClientMetadataOutcome> Ensure(string clientId = Url, string? redirect = Callback)
        {
            using var scope = Provider.CreateScope();
            var service = new ClientMetadataService(
                scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>(), Fetcher, Options, Failures, Time);
            return await service.EnsureAsync(clientId, redirect, Ct);
        }

        public async Task<OpenIddictApplicationDescriptor?> Row(string clientId = Url)
        {
            using var scope = Provider.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var app = await manager.FindByClientIdAsync(clientId, Ct);
            if (app is null) return null;
            var descriptor = new OpenIddictApplicationDescriptor();
            await manager.PopulateAsync(descriptor, app, Ct);
            return descriptor;
        }

        public int RowCount()
        {
            using var scope = Provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>()
                .Set<OpenIddictEntityFrameworkCoreApplication<Guid>>().Count();
        }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private static string Doc(string name = "Claude", string callback = Callback) =>
        $$"""{"client_id":"{{Url}}","client_name":"{{name}}","redirect_uris":["{{callback}}"]}""";

    [Fact]
    public async Task First_sight_fetches_and_stores_a_public_pkce_client_that_asks_for_consent()
    {
        await using var rig = new Rig();

        var outcome = await rig.Ensure();

        Assert.True(outcome.Ready);
        var row = (await rig.Row())!;
        Assert.Equal(ClientTypes.Public, row.ClientType);
        Assert.Equal(ConsentTypes.Explicit, row.ConsentType);
        Assert.Equal("Claude", row.DisplayName);
        Assert.Equal([new Uri(Callback)], row.RedirectUris);
        Assert.Contains(Requirements.Features.ProofKeyForCodeExchange, row.Requirements);
        Assert.Contains(Permissions.GrantTypes.AuthorizationCode, row.Permissions);
        Assert.DoesNotContain(Permissions.GrantTypes.ClientCredentials, row.Permissions);
        Assert.True(row.Properties.ContainsKey("cimd_fetched_utc"));
        await rig.Fetcher.Received(1).FetchAsync(new Uri(Url), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_fresh_row_is_served_without_another_fetch()
    {
        await using var rig = new Rig();
        await rig.Ensure();
        rig.Time.Advance(TimeSpan.FromMinutes(30));

        Assert.True((await rig.Ensure()).Ready);

        await rig.Fetcher.Received(1).FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_stale_row_is_refreshed_in_place_and_picks_up_changes()
    {
        await using var rig = new Rig();
        await rig.Ensure();
        var first = rig.RowCount();
        rig.Time.Advance(TimeSpan.FromHours(2));
        rig.Respond(Doc(name: "Claude (new)", callback: "https://claude.ai/api/mcp/auth_callback2"));

        Assert.True((await rig.Ensure(redirect: null)).Ready);

        Assert.Equal(first, rig.RowCount());
        var row = (await rig.Row())!;
        Assert.Equal("Claude (new)", row.DisplayName);
        Assert.Equal([new Uri("https://claude.ai/api/mcp/auth_callback2")], row.RedirectUris);
    }

    [Fact]
    public async Task A_redirect_the_row_does_not_know_triggers_one_refetch_but_not_a_storm()
    {
        await using var rig = new Rig();
        await rig.Ensure();
        rig.Time.Advance(TimeSpan.FromMinutes(2));
        rig.Respond(Doc(callback: "https://claude.ai/api/mcp/new_callback"));

        await rig.Ensure(redirect: "https://claude.ai/api/mcp/new_callback");
        await rig.Ensure(redirect: "https://claude.ai/api/mcp/new_callback");
        await rig.Ensure(redirect: "https://claude.ai/api/mcp/unlisted");
        await rig.Ensure(redirect: "https://claude.ai/api/mcp/unlisted");

        await rig.Fetcher.Received(2).FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("https://evil.example.test/oauth/client")]
    [InlineData("https://claude.ai.evil.test/oauth/client")]
    [InlineData("https://sub.claude.ai/oauth/client")]
    public async Task Hosts_outside_the_allow_list_are_never_fetched(string clientId)
    {
        await using var rig = new Rig();

        var outcome = await rig.Ensure(clientId);

        Assert.False(outcome.Ready);
        await rig.Fetcher.DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
        Assert.Equal(0, rig.RowCount());
    }

    [Theory]
    [InlineData("http://claude.ai/oauth/client")]
    [InlineData("https://claude.ai/")]
    [InlineData("dfrnt-claude")]
    [InlineData("https://127.0.0.1/x")]
    public async Task Unacceptable_urls_are_never_fetched(string clientId)
    {
        await using var rig = new Rig("claude.ai", "127.0.0.1");

        Assert.False((await rig.Ensure(clientId)).Ready);
        await rig.Fetcher.DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
    }

    [Fact]
    public async Task With_no_allowed_hosts_the_feature_is_off()
    {
        await using var rig = new Rig();
        var off = new ClientMetadataService(Substitute.For<IOpenIddictApplicationManager>(), rig.Fetcher, ClientMetadataOptions.Create([]), rig.Failures, rig.Time);

        Assert.False((await off.EnsureAsync(Url, Callback, Ct)).Ready);
        await rig.Fetcher.DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
    }

    [Fact]
    public async Task A_failed_fetch_creates_nothing_and_is_not_retried_for_five_minutes()
    {
        await using var rig = new Rig();
        rig.Fail();

        Assert.False((await rig.Ensure()).Ready);
        Assert.False((await rig.Ensure()).Ready);
        Assert.Equal(0, rig.RowCount());
        await rig.Fetcher.Received(1).FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_bad_document_creates_nothing()
    {
        await using var rig = new Rig();
        rig.Respond("""{"client_id":"https://claude.ai/oauth/other","client_name":"x","redirect_uris":["https://claude.ai/cb"]}""");

        Assert.False((await rig.Ensure()).Ready);
        Assert.Equal(0, rig.RowCount());
    }

    [Fact]
    public async Task A_refresh_that_fails_to_connect_keeps_the_last_good_row_for_two_hours_and_then_stops()
    {
        await using var rig = new Rig();
        await rig.Ensure();
        rig.Fail();

        rig.Time.Advance(TimeSpan.FromMinutes(70));
        Assert.True((await rig.Ensure()).Ready);

        rig.Time.Advance(TimeSpan.FromHours(2));
        rig.Failures.Forget(Url);
        Assert.False((await rig.Ensure()).Ready);
    }

    [Fact]
    public async Task A_row_that_was_not_created_from_a_document_is_never_touched()
    {
        await using var rig = new Rig();
        using (var scope = rig.Provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(
                new OpenIddictApplicationDescriptor { ClientId = Url, DisplayName = "Hand registered" }, Ct);
        }

        Assert.True((await rig.Ensure()).Ready);

        Assert.Equal("Hand registered", (await rig.Row())!.DisplayName);
        await rig.Fetcher.DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
    }

    [Theory]
    [InlineData("https://evil.example.test/cb")]
    [InlineData("https://claude.ai.evil.test/cb")]
    [InlineData("https://sub.claude.ai/cb")]
    public async Task A_redirect_uri_on_a_host_that_is_not_trusted_rejects_the_whole_document(string callback)
    {
        await using var rig = new Rig();
        rig.Respond(Doc(callback: callback));

        Assert.False((await rig.Ensure(redirect: null)).Ready);
        Assert.Equal(0, rig.RowCount());
    }

    [Fact]
    public async Task A_document_that_was_reached_but_is_no_longer_acceptable_ends_the_client_at_once()
    {
        await using var rig = new Rig();
        await rig.Ensure();
        rig.Time.Advance(TimeSpan.FromMinutes(90));
        rig.Respond("""{"client_id":"https://claude.ai/oauth/mcp-client","client_name":"x","redirect_uris":["https://claude.ai/cb"],"client_secret":"s"}""");

        Assert.False((await rig.Ensure(redirect: null)).Ready);
    }

    [Fact]
    public async Task A_url_with_a_query_string_is_not_a_client_id()
    {
        await using var rig = new Rig();

        Assert.False((await rig.Ensure(Url + "?x=1")).Ready);
        await rig.Fetcher.DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
    }
}
