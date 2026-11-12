using Hub.Features.AgentAccess.Capabilities;
using Hub.Interfaces;
using Hub.Models;
using Hub.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Capabilities;

// What an AI app may do for a contact: the role's agent-* verb AND, for exports, the hub tile of the app the data comes
// from, decided exactly as the Hub launcher decides it. Verbs follow the configurator's RolePermissionResolver rules
// (per-client override, union across roles, cascade from an ancestor, explicit None on an ancestor denies).
public partial class AgentCapabilityResolverTests
{
    private const string IntegrationManager = "hub-tile-integration-manager";
    private const string DespatchWeb = "hub-tile-despatchweb";
    private const string Configurator = "hub-tile-configurator";

    private const int ContactId = 100;
    private const int ClientId = 200;
    private const int OtherClientId = 201;
    private const int RoleA = 1;
    private const int RoleB = 2;

    private static readonly string[] AllTiles = [IntegrationManager, DespatchWeb, Configurator];

    [Fact]
    public async Task A_role_with_only_the_default_deny_rows_gets_nothing()
    {
        await using var ctx = NewContext(nameof(A_role_with_only_the_default_deny_rows_gets_nothing));
        Seed(ctx, roles: [RoleA]);
        DenyAllVerbs(ctx, RoleA);
        await ctx.SaveChangesAsync(Ct);

        Assert.Empty(await Resolve(ctx));
    }

    [Fact]
    public async Task Connect_alone_allows_only_connect()
    {
        await using var ctx = NewContext(nameof(Connect_alone_allows_only_connect));
        Seed(ctx, roles: [RoleA]);
        DenyAllVerbs(ctx, RoleA);
        Set(ctx, RoleA, AgentCapabilities.Connect, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal([AgentCapabilities.Connect], await Resolve(ctx));
    }

    [Fact]
    public async Task An_export_verb_without_connect_is_not_granted()
    {
        await using var ctx = NewContext(nameof(An_export_verb_without_connect_is_not_granted));
        Seed(ctx, roles: [RoleA]);
        DenyAllVerbs(ctx, RoleA);
        Set(ctx, RoleA, AgentCapabilities.ExportJobs, 1);
        Set(ctx, RoleA, AgentCapabilities.SeeCosts, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Empty(await Resolve(ctx));
    }

    [Fact]
    public async Task Exports_follow_the_tile_of_the_app_they_read_from()
    {
        // The role is configured for tiles (has tile rows) and holds Despatch Web but not the configurator: it may export
        // jobs, not rates, whatever its verbs say.
        await using var ctx = NewContext(nameof(Exports_follow_the_tile_of_the_app_they_read_from));
        Seed(ctx, roles: [RoleA]);
        GrantAllVerbs(ctx, RoleA);
        Set(ctx, RoleA, IntegrationManager, 1);
        Set(ctx, RoleA, DespatchWeb, 1);
        Set(ctx, RoleA, Configurator, 0);
        await ctx.SaveChangesAsync(Ct);

        var result = await Resolve(ctx);

        Assert.Contains(AgentCapabilities.ExportJobs, result);
        Assert.DoesNotContain(AgentCapabilities.ExportPricing, result);
        Assert.Contains(AgentCapabilities.SeeCosts, result);
    }

    [Fact]
    public async Task An_unconfigured_role_keeps_the_tiles_the_launcher_shows_it()
    {
        // Hub gate 2 fails open for a role with no tile rows (the rollout state of every tenant today), so exports need
        // only the verbs and gate 1.
        await using var ctx = NewContext(nameof(An_unconfigured_role_keeps_the_tiles_the_launcher_shows_it));
        Seed(ctx, roles: [RoleA]);
        GrantAllVerbs(ctx, RoleA);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(AgentCapabilities.All.Order(), (await Resolve(ctx)).Order());
    }

    [Fact]
    public async Task A_tile_the_tenant_does_not_have_blocks_its_export()
    {
        await using var ctx = NewContext(nameof(A_tile_the_tenant_does_not_have_blocks_its_export));
        Seed(ctx, roles: [RoleA]);
        GrantAllVerbs(ctx, RoleA);
        await ctx.SaveChangesAsync(Ct);

        var result = await Resolve(ctx, enabledTiles: [IntegrationManager, DespatchWeb]);

        Assert.Contains(AgentCapabilities.ExportJobs, result);
        Assert.DoesNotContain(AgentCapabilities.ExportPricing, result);
    }

    [Fact]
    public async Task A_verb_with_no_row_of_its_own_is_off_whatever_its_ancestors_grant()
    {
        // Hub fails closed for agent-* verbs: a tile grant must never cascade a verb on.
        await using var ctx = NewContext(nameof(A_verb_with_no_row_of_its_own_is_off_whatever_its_ancestors_grant));
        Seed(ctx, roles: [RoleA]);
        Set(ctx, RoleA, IntegrationManager, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Empty(await Resolve(ctx));
    }

    [Fact]
    public async Task An_explicit_none_on_an_ancestor_denies_a_granted_verb()
    {
        await using var ctx = NewContext(nameof(An_explicit_none_on_an_ancestor_denies_a_granted_verb));
        Seed(ctx, roles: [RoleA]);
        GrantAllVerbs(ctx, RoleA);
        Set(ctx, RoleA, AgentCapabilities.ParentKey, 0);
        await ctx.SaveChangesAsync(Ct);

        Assert.Empty(await Resolve(ctx));
    }

    [Fact]
    public async Task Stacked_roles_add_up()
    {
        await using var ctx = NewContext(nameof(Stacked_roles_add_up));
        Seed(ctx, roles: [RoleA, RoleB]);
        DenyAllVerbs(ctx, RoleA);
        DenyAllVerbs(ctx, RoleB);
        Set(ctx, RoleA, AgentCapabilities.Connect, 1);
        Set(ctx, RoleB, AgentCapabilities.ExportJobs, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(
            new[] { AgentCapabilities.Connect, AgentCapabilities.ExportJobs }.Order(),
            (await Resolve(ctx)).Order());
    }
    private static void AddDuplicate(DynamicDespatchDbContext ctx, int roleId, string key, byte level, int? clientId = null) =>
        ctx.RolePermissions.Add(new RolePermission
        {
            RolePermissionId = Interlocked.Increment(ref nextRowId),
            ContactRoleId = roleId, PermissionKey = key, Allowed = level >= 1, AccessLevel = level, ClientId = clientId
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_rows_for_the_same_role_key_and_scope_take_the_lowest_level_in_either_order(bool denyFirst)
    {
        await using var ctx = NewContext(nameof(Duplicate_rows_for_the_same_role_key_and_scope_take_the_lowest_level_in_either_order) + denyFirst);
        Seed(ctx, roles: [RoleA]);
        DenyAllVerbs(ctx, RoleA);
        // Connect: a stray allow beside an explicit deny (global rows), and the same on the client override.
        ctx.RolePermissions.RemoveRange(ctx.RolePermissions.Local.Where(r => r.PermissionKey == AgentCapabilities.Connect).ToList());
        if (denyFirst) AddDuplicate(ctx, RoleA, AgentCapabilities.Connect, 0);
        AddDuplicate(ctx, RoleA, AgentCapabilities.Connect, 1);
        if (!denyFirst) AddDuplicate(ctx, RoleA, AgentCapabilities.Connect, 0);
        await ctx.SaveChangesAsync(Ct);

        Assert.Empty(await Resolve(ctx));
    }

    [Fact]
    public async Task Duplicate_client_overrides_take_the_lowest_level_and_still_beat_the_global_row()
    {
        await using var ctx = NewContext(nameof(Duplicate_client_overrides_take_the_lowest_level_and_still_beat_the_global_row));
        Seed(ctx, roles: [RoleA]);
        DenyAllVerbs(ctx, RoleA);
        Set(ctx, RoleA, AgentCapabilities.Connect, 1);
        AddDuplicate(ctx, RoleA, AgentCapabilities.Connect, 1, ClientId);
        AddDuplicate(ctx, RoleA, AgentCapabilities.Connect, 0, ClientId);
        await ctx.SaveChangesAsync(Ct);

        Assert.Empty(await Resolve(ctx));
    }


    [Fact]
    public async Task A_granted_agent_access_parent_without_rows_on_the_verbs_grants_nothing()
    {
        await using var ctx = NewContext(nameof(A_granted_agent_access_parent_without_rows_on_the_verbs_grants_nothing));
        Seed(ctx, roles: [RoleA]);
        Set(ctx, RoleA, AgentCapabilities.ParentKey, 2);
        await ctx.SaveChangesAsync(Ct);

        Assert.Empty(await Resolve(ctx));
    }

    [Fact]
    public async Task A_verb_with_its_own_row_is_granted_beside_a_granted_parent_but_its_unlisted_siblings_are_not()
    {
        await using var ctx = NewContext(nameof(A_verb_with_its_own_row_is_granted_beside_a_granted_parent_but_its_unlisted_siblings_are_not));
        Seed(ctx, roles: [RoleA]);
        Set(ctx, RoleA, AgentCapabilities.ParentKey, 2);
        Set(ctx, RoleA, AgentCapabilities.Connect, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal([AgentCapabilities.Connect], await Resolve(ctx));
    }

    [Fact]
    public async Task A_per_client_override_beats_the_global_row_for_that_client_only()
    {
        await using var ctx = NewContext(nameof(A_per_client_override_beats_the_global_row_for_that_client_only));
        Seed(ctx, roles: [RoleA]);
        DenyAllVerbs(ctx, RoleA);
        Set(ctx, RoleA, AgentCapabilities.Connect, 1, clientId: ClientId);
        Set(ctx, RoleA, AgentCapabilities.ExportJobs, 1, clientId: OtherClientId);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal([AgentCapabilities.Connect], await Resolve(ctx));
    }

    [Fact]
    public async Task An_inactive_role_counts_for_nothing()
    {
        await using var ctx = NewContext(nameof(An_inactive_role_counts_for_nothing));
        Seed(ctx, roles: [RoleA], inactive: [RoleA]);
        GrantAllVerbs(ctx, RoleA);
        await ctx.SaveChangesAsync(Ct);

        Assert.Empty(await Resolve(ctx));
    }

    [Fact]
    public async Task The_legacy_single_role_is_used_when_the_contact_has_no_stacked_roles()
    {
        await using var ctx = NewContext(nameof(The_legacy_single_role_is_used_when_the_contact_has_no_stacked_roles));
        Seed(ctx, roles: [], legacyRole: RoleA);
        DenyAllVerbs(ctx, RoleA);
        Set(ctx, RoleA, AgentCapabilities.Connect, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal([AgentCapabilities.Connect], await Resolve(ctx));
    }

    [Fact]
    public async Task A_contact_with_no_role_gets_nothing()
    {
        await using var ctx = NewContext(nameof(A_contact_with_no_role_gets_nothing));
        Seed(ctx, roles: []);
        await ctx.SaveChangesAsync(Ct);

        Assert.Empty(await Resolve(ctx));
    }

    [Fact]
    public async Task DF_admin_gets_everything()
    {
        await using var ctx = NewContext(nameof(DF_admin_gets_everything));
        Seed(ctx, roles: [RoleA], clientTypeId: 5);
        DenyAllVerbs(ctx, RoleA);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(AgentCapabilities.All.Order(), (await Resolve(ctx, enabledTiles: [])).Order());
    }

    // ---- helpers ----------------------------------------------------------

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<IReadOnlySet<string>> Resolve(DynamicDespatchDbContext ctx, string[]? enabledTiles = null)
        => (await ResolveDetailed(ctx, enabledTiles)).Capabilities;

    private static async Task<AgentCapabilityDecision> ResolveDetailed(DynamicDespatchDbContext ctx, string[]? enabledTiles = null)
    {
        var features = Substitute.For<IFeatureResolver>();
        features.ResolveForClientAsync(ClientId, true, "NZ")
            .Returns(new HashSet<string>(enabledTiles ?? AllTiles, StringComparer.OrdinalIgnoreCase));

        return await new AgentCapabilityResolver(ctx, features, new TileAccessResolver(ctx))
            .ResolveDetailedAsync(new AgentCapabilitySubject(ContactId, ClientId, Internal: true, CountryCode: "NZ"), Ct);
    }

    private static void GrantAllVerbs(DynamicDespatchDbContext ctx, int roleId)
    {
        foreach (var verb in AgentCapabilities.All) Set(ctx, roleId, verb, 1);
    }

    private static void DenyAllVerbs(DynamicDespatchDbContext ctx, int roleId)
    {
        foreach (var verb in AgentCapabilities.All) Set(ctx, roleId, verb, 0);
    }

    private static int nextRowId;

    private static void Set(DynamicDespatchDbContext ctx, int roleId, string key, byte level, int? clientId = null)
    {
        // A global row and an override for the same (role, key) are separate rows; replace an earlier global one.
        var existing = ctx.RolePermissions.Local.SingleOrDefault(r => r.ContactRoleId == roleId && r.PermissionKey == key && r.ClientId == clientId);
        if (existing is not null) ctx.RolePermissions.Remove(existing);

        ctx.RolePermissions.Add(new RolePermission
        {
            RolePermissionId = Interlocked.Increment(ref nextRowId),
            ContactRoleId = roleId,
            PermissionKey = key,
            Allowed = level >= 1,
            AccessLevel = level,
            ClientId = clientId
        });
    }

    private static void Seed(
        DynamicDespatchDbContext ctx, int[] roles, int[]? inactive = null, int? legacyRole = null, int clientTypeId = 4)
    {
        foreach (var tile in AllTiles) ctx.Features.Add(NewFeature(tile, "HubTile", parent: null));
        ctx.Features.Add(NewFeature(AgentCapabilities.ParentKey, "MenuItem", IntegrationManager));
        foreach (var verb in AgentCapabilities.All) ctx.Features.Add(NewFeature(verb, "RolePermission", AgentCapabilities.ParentKey));
        foreach (var screen in AgentToolCatalogue.Tools.SelectMany(t => t.ScreenKeys).Distinct())
        {
            ctx.Features.Add(NewFeature(screen, "RolePermission", DespatchWeb));
        }

        foreach (var roleId in new[] { RoleA, RoleB })
        {
            ctx.TblContactRoles.Add(new TblContactRole { ContactRoleId = roleId, IsActive = !(inactive ?? []).Contains(roleId) });
        }

        foreach (var roleId in roles)
        {
            ctx.TblContactContactRoles.Add(new TblContactContactRole { ClientContactId = ContactId, ContactRoleId = roleId });
        }

        ctx.TucClientContacts.Add(new TucClientContact
        {
            UcctId = ContactId,
            UcctClientId = ClientId,
            ContactRoleId = legacyRole,
            CreatedBy = Stub, LastModifiedBy = Stub
        });

        ctx.TucClients.Add(new TucClient
        {
            UcclId = ClientId,
            ClientTypeId = clientTypeId,
            CreatedBy = Stub, LastModifiedBy = Stub, Smsname = Stub,
            UcclCode = Stub, UcclLegalName = Stub, UcclName = Stub
        });
    }

    private const string Stub = "test";

    private static Feature NewFeature(string key, string category, string? parent)
        => new() { FeatureKey = key, DisplayName = key, Description = Stub, Category = category, ParentKey = parent };

    private static DynamicDespatchDbContext NewContext(string name)
    {
        var options = new DbContextOptionsBuilder<DespatchContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new DynamicDespatchDbContext(options, Substitute.For<IConnectionStringManager>());
    }
}
