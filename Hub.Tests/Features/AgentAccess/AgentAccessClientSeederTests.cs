using Hub.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Hub.Tests.Services;
using Hub.Features.AgentAccess;

namespace Hub.Tests.Features.AgentAccess;

// Phase 1.4: pre-registered OAuth clients come from configuration and are created or updated in the
// McpOAuthApplication table, only once the agent access gate has opened for some tenant. The seeder
// never deletes, never touches rows it does not own, and never throws.
public class AgentAccessClientSeederTests
{
    private static AgentAccessClientOptions Claude() => new()
    {
        ClientId = "dfrnt-claude",
        DisplayName = "Claude",
        RedirectUris = ["https://claude.ai/api/mcp/auth_callback", "https://claude.com/api/mcp/auth_callback"]
    };

    [Fact]
    public async Task Creates_a_missing_client_as_a_public_explicit_pkce_client()
    {
        await using var provider = Provider(nameof(Creates_a_missing_client_as_a_public_explicit_pkce_client));

        await Seeder(provider, Claude()).EnsureAsync(TestContext.Current.CancellationToken);

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var app = await manager.FindByClientIdAsync("dfrnt-claude", TestContext.Current.CancellationToken);

        Assert.NotNull(app);
        Assert.Equal(OpenIddictConstants.ClientTypes.Public, await manager.GetClientTypeAsync(app!));
        Assert.Equal(OpenIddictConstants.ConsentTypes.Explicit, await manager.GetConsentTypeAsync(app!));
        Assert.Equal("Claude", await manager.GetDisplayNameAsync(app!));
        Assert.Equal(
            [new Uri("https://claude.ai/api/mcp/auth_callback"), new Uri("https://claude.com/api/mcp/auth_callback")],
            (await manager.GetRedirectUrisAsync(app!)).Select(u => new Uri(u)).OrderBy(u => u.Host));
        Assert.Contains(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange, await manager.GetRequirementsAsync(app!));

        var permissions = await manager.GetPermissionsAsync(app!);
        Assert.Contains(OpenIddictConstants.Permissions.Endpoints.Authorization, permissions);
        Assert.Contains(OpenIddictConstants.Permissions.Endpoints.Token, permissions);
        Assert.Contains(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode, permissions);
        Assert.Contains(OpenIddictConstants.Permissions.GrantTypes.RefreshToken, permissions);
        Assert.Contains(OpenIddictConstants.Permissions.ResponseTypes.Code, permissions);
        Assert.Contains(OpenIddictConstants.Permissions.Prefixes.Scope + "mcp", permissions);
        Assert.Contains(OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess, permissions);
        Assert.DoesNotContain(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials, permissions);
        Assert.DoesNotContain(OpenIddictConstants.Permissions.GrantTypes.Password, permissions);
        Assert.DoesNotContain(OpenIddictConstants.Permissions.GrantTypes.Implicit, permissions);
    }

    [Fact]
    public async Task Running_again_changes_nothing()
    {
        await using var provider = Provider(nameof(Running_again_changes_nothing));
        await Seeder(provider, Claude()).EnsureAsync(TestContext.Current.CancellationToken);
        var before = Snapshot(provider);

        // A fresh seeder, as after a restart: it must find the row identical and leave it alone.
        await Seeder(provider, Claude()).EnsureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(before, Snapshot(provider));
    }

    [Fact]
    public async Task A_changed_configuration_updates_the_existing_row_in_place()
    {
        await using var provider = Provider(nameof(A_changed_configuration_updates_the_existing_row_in_place));
        await Seeder(provider, Claude()).EnsureAsync(TestContext.Current.CancellationToken);
        var before = Snapshot(provider);

        var changed = Claude() with { RedirectUris = ["https://claude.ai/api/mcp/auth_callback"], DisplayName = "Claude (web)" };
        await Seeder(provider, changed).EnsureAsync(TestContext.Current.CancellationToken);

        var after = Snapshot(provider);
        Assert.Single(after);
        Assert.Equal(before.Single().Id, after.Single().Id);
        Assert.NotEqual(before.Single().ConcurrencyToken, after.Single().ConcurrencyToken);

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var app = await manager.FindByClientIdAsync("dfrnt-claude", TestContext.Current.CancellationToken);
        Assert.Equal("Claude (web)", await manager.GetDisplayNameAsync(app!));
        Assert.Single(await manager.GetRedirectUrisAsync(app!));
    }

    [Fact]
    public async Task Rows_that_are_not_in_the_configuration_are_left_alone()
    {
        await using var provider = Provider(nameof(Rows_that_are_not_in_the_configuration_are_left_alone));
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(
                new OpenIddictApplicationDescriptor { ClientId = "https://example.test/cimd.json", DisplayName = "Cached" },
                TestContext.Current.CancellationToken);
        }

        await Seeder(provider, Claude()).EnsureAsync(TestContext.Current.CancellationToken);

        var rows = Snapshot(provider);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ClientId == "https://example.test/cimd.json");
    }

    [Fact]
    public async Task Several_clients_are_created()
    {
        await using var provider = Provider(nameof(Several_clients_are_created));
        var second = new AgentAccessClientOptions
        {
            ClientId = "dfrnt-other", DisplayName = "Other", RedirectUris = ["https://other.example.test/callback"]
        };

        await Seeder(provider, Claude(), second).EnsureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["dfrnt-claude", "dfrnt-other"], Snapshot(provider).Select(r => r.ClientId).Order().ToArray());
    }

    [Theory]
    [InlineData("")]                                   // no id
    [InlineData("https://example.test/client.json")]   // URL-shaped ids belong to client-metadata documents
    [InlineData("dfrnt-mcp")]                          // reserved for the confidential token-exchange client
    public async Task An_invalid_client_id_is_skipped_without_blocking_valid_clients(string clientId)
    {
        await using var provider = Provider("invalid-id-" + clientId.Length);

        await Seeder(provider, Claude() with { ClientId = clientId }, Claude() with { ClientId = "dfrnt-valid" })
            .EnsureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["dfrnt-valid"], Snapshot(provider).Select(r => r.ClientId).ToArray());
    }

    [Theory]
    [InlineData("http://claude.ai/api/mcp/auth_callback")]       // https only
    [InlineData("https://claude.ai/callback#fragment")]          // no fragments (RFC 6749)
    [InlineData("not a uri")]
    [InlineData("/relative/callback")]
    public async Task A_bad_redirect_uri_makes_the_client_invalid(string redirect)
    {
        await using var provider = Provider("bad-redirect-" + redirect.GetHashCode());

        await Seeder(provider, Claude() with { RedirectUris = [redirect] }).EnsureAsync(TestContext.Current.CancellationToken);

        Assert.Empty(Snapshot(provider));
    }

    [Fact]
    public async Task A_client_with_no_redirect_uris_or_no_display_name_is_skipped()
    {
        await using var provider = Provider(nameof(A_client_with_no_redirect_uris_or_no_display_name_is_skipped));

        await Seeder(provider, Claude() with { RedirectUris = [] }, Claude() with { ClientId = "no-name", DisplayName = " " })
            .EnsureAsync(TestContext.Current.CancellationToken);

        Assert.Empty(Snapshot(provider));
    }

    [Fact]
    public async Task A_duplicate_client_id_in_the_configuration_is_seeded_once()
    {
        await using var provider = Provider(nameof(A_duplicate_client_id_in_the_configuration_is_seeded_once));

        await Seeder(provider, Claude(), Claude() with { DisplayName = "Duplicate" })
            .EnsureAsync(TestContext.Current.CancellationToken);

        var row = Assert.Single(Snapshot(provider));
        Assert.Equal("dfrnt-claude", row.ClientId);
    }

    [Fact]
    public async Task With_no_clients_configured_the_database_is_never_touched()
    {
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ => throw new InvalidOperationException("must not open a scope"));

        await new AgentAccessClientSeeder(scopes, []).EnsureAsync(TestContext.Current.CancellationToken);

        scopes.DidNotReceive().CreateScope();
    }

    [Fact]
    public async Task Concurrent_calls_seed_once()
    {
        await using var provider = Provider(nameof(Concurrent_calls_seed_once));
        var seeder = Seeder(provider, Claude());

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => seeder.EnsureAsync(TestContext.Current.CancellationToken)));

        Assert.Single(Snapshot(provider));
    }

    [Fact]
    public async Task After_success_later_calls_do_not_touch_the_database()
    {
        await using var provider = Provider(nameof(After_success_later_calls_do_not_touch_the_database));
        var counting = new CountingScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var seeder = new AgentAccessClientSeeder(counting, [Claude()]);

        await seeder.EnsureAsync(TestContext.Current.CancellationToken);
        await seeder.EnsureAsync(TestContext.Current.CancellationToken);
        await seeder.EnsureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, counting.Scopes);
    }

    [Fact]
    public async Task A_database_failure_never_throws_and_is_retried_only_after_a_backoff()
    {
        await using var provider = Provider(nameof(A_database_failure_never_throws_and_is_retried_only_after_a_backoff));
        var clock = new ManualTimeProvider();
        var flaky = new CountingScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()) { FailNext = 1 };
        var seeder = new AgentAccessClientSeeder(flaky, [Claude()], clock);

        await seeder.EnsureAsync(TestContext.Current.CancellationToken);   // fails, swallowed
        await seeder.EnsureAsync(TestContext.Current.CancellationToken);   // inside the backoff: not attempted
        Assert.Equal(1, flaky.Attempts);
        Assert.Empty(Snapshot(provider));

        clock.Advance(TimeSpan.FromMinutes(2));
        await seeder.EnsureAsync(TestContext.Current.CancellationToken);   // retried, succeeds

        Assert.Equal(2, flaky.Attempts);
        Assert.Single(Snapshot(provider));
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        await using var provider = Provider(nameof(Cancellation_is_not_swallowed));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Seeder(provider, Claude()).EnsureAsync(cts.Token));
    }

    [Fact]
    public async Task The_clients_shipped_in_appsettings_bind_and_seed()
    {
        // A bad default would be skipped silently at runtime, so pin it here.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();
        var clients = configuration.GetSection("AgentAccess:Clients").Get<List<AgentAccessClientOptions>>();
        Assert.NotNull(clients);
        Assert.NotEmpty(clients!);

        await using var provider = Provider(nameof(The_clients_shipped_in_appsettings_bind_and_seed));
        await new AgentAccessClientSeeder(provider.GetRequiredService<IServiceScopeFactory>(), clients!)
            .EnsureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(clients!.Select(c => c.ClientId).Order().ToArray(), Snapshot(provider).Select(r => r.ClientId).Order().ToArray());
    }

    // ---- helpers ----------------------------------------------------------

    private static ServiceProvider Provider(string databaseName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AgentAccessOpenIddictDbContext>(options =>
        {
            options.UseInMemoryDatabase(databaseName);
            options.UseOpenIddict<Guid>();
        });
        services.AddOpenIddict().AddCore(core => core
            .UseEntityFrameworkCore()
            .UseDbContext<AgentAccessOpenIddictDbContext>()
            .ReplaceDefaultEntities<Guid>());
        return services.BuildServiceProvider();
    }

    private static AgentAccessClientSeeder Seeder(ServiceProvider provider, params AgentAccessClientOptions[] clients) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), clients);

    private sealed record Row(Guid Id, string? ClientId, string? ConcurrencyToken);

    private static List<Row> Snapshot(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
        return ctx.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>()
            .AsNoTracking()
            .Select(a => new Row(a.Id, a.ClientId, a.ConcurrencyToken))
            .ToList();
    }

    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public int Scopes { get; private set; }
        public int Attempts { get; private set; }
        public int FailNext { get; set; }

        public IServiceScope CreateScope()
        {
            Attempts++;
            if (FailNext > 0)
            {
                FailNext--;
                throw new InvalidOperationException("database down");
            }

            Scopes++;
            return inner.CreateScope();
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
