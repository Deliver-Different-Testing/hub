using Hub;
using Hub.Interfaces;
using Hub.Models;
using Hub.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Hub.Tests.Services;

// Gate 2 of the tile-level access model. The behaviours that matter are the
// ones nobody can see from the outside: a missing tile looks identical whether
// the tenant never had it, the role was not granted it, or nothing is
// configured yet - so every case is asserted on its REASON, not just on
// granted/not-granted.
public class TileAccessResolverTests
{
    private const string Configurator = "hub-tile-configurator";
    private const string DespatchWeb = "hub-tile-despatchweb";
    private const string Tracking = "hub-tile-tracking";

    private const int ContactId = 100;
    private const int ClientId = 200;
    private const int AdminRole = 1;
    private const int DispatcherRole = 2;

    [Fact]
    public async Task Unconfigured_roles_do_not_restrict_anything()
    {
        // The rollout case: no tile grants exist anywhere yet. Enforcing here
        // would take every tile from every user the moment this deploys.
        await using var ctx = NewContext(nameof(Unconfigured_roles_do_not_restrict_anything));
        Seed(ctx, roleIdsForContact: [AdminRole]);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await Resolve(ctx, enabled: [Configurator, DespatchWeb]);

        Assert.True(Granted(result, Configurator));
        Assert.Equal(TileAccessReason.NotConfigured, Reason(result, Configurator));
        Assert.True(Granted(result, DespatchWeb));
    }

    [Fact]
    public async Task A_role_denied_every_tile_is_enforced_not_treated_as_unconfigured()
    {
        // REGRESSION. urgent-staging has exactly this: NP Dispatcher (role 6)
        // with a row for every tile, all AccessLevel 0 / Allowed 0. Deciding
        // "configured" on whether any row GRANTS would read that as untouched
        // and hand the role every tile - the opposite of what those rows say.
        // Configuration is row existence, not row content.
        await using var ctx = NewContext(
            nameof(A_role_denied_every_tile_is_enforced_not_treated_as_unconfigured));
        Seed(ctx, roleIdsForContact: [DispatcherRole]);
        foreach (var tile in new[] { Configurator, DespatchWeb, Tracking })
        {
            ctx.RolePermissions.Add(Grant(DispatcherRole, tile, level: 0));
        }

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await Resolve(ctx, enabled: [Configurator, DespatchWeb, Tracking]);

        Assert.All(result, d => Assert.False(d.Granted));
        Assert.All(result, d => Assert.Equal(TileAccessReason.RoleLacksTile, d.Reason));
    }

    [Fact]
    public async Task Configured_role_is_restricted_to_its_granted_tiles()
    {
        await using var ctx = NewContext(nameof(Configured_role_is_restricted_to_its_granted_tiles));
        Seed(ctx, roleIdsForContact: [DispatcherRole]);
        ctx.RolePermissions.Add(Grant(DispatcherRole, DespatchWeb));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await Resolve(ctx, enabled: [Configurator, DespatchWeb]);

        Assert.True(Granted(result, DespatchWeb));
        Assert.Equal(TileAccessReason.Granted, Reason(result, DespatchWeb));

        Assert.False(Granted(result, Configurator));
        Assert.Equal(TileAccessReason.RoleLacksTile, Reason(result, Configurator));
    }

    [Fact]
    public async Task Gate_one_closing_is_reported_distinctly_from_gate_two()
    {
        // Tracking is granted to the role but NOT enabled for the tenant. The
        // user sees no tile either way; support needs to know which gate it was.
        await using var ctx = NewContext(nameof(Gate_one_closing_is_reported_distinctly_from_gate_two));
        Seed(ctx, roleIdsForContact: [DispatcherRole]);
        ctx.RolePermissions.Add(Grant(DispatcherRole, DespatchWeb));
        ctx.RolePermissions.Add(Grant(DispatcherRole, Tracking));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await Resolve(ctx, enabled: [Configurator, DespatchWeb]);

        Assert.False(Granted(result, Tracking));
        Assert.Equal(TileAccessReason.NotEnabledForTenant, Reason(result, Tracking));
    }

    [Fact]
    public async Task Stacked_roles_union_their_grants()
    {
        await using var ctx = NewContext(nameof(Stacked_roles_union_their_grants));
        Seed(ctx, roleIdsForContact: [AdminRole, DispatcherRole]);
        ctx.RolePermissions.Add(Grant(AdminRole, Configurator));
        ctx.RolePermissions.Add(Grant(DispatcherRole, DespatchWeb));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await Resolve(ctx, enabled: [Configurator, DespatchWeb]);

        Assert.True(Granted(result, Configurator));
        Assert.True(Granted(result, DespatchWeb));
    }

    [Fact]
    public async Task Inactive_roles_are_ignored()
    {
        await using var ctx = NewContext(nameof(Inactive_roles_are_ignored));
        Seed(ctx, roleIdsForContact: [DispatcherRole], inactiveRoleIds: [DispatcherRole]);
        ctx.RolePermissions.Add(Grant(DispatcherRole, DespatchWeb));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await Resolve(ctx, enabled: [Configurator, DespatchWeb]);

        // No ACTIVE role, so no grants resolve, so gate 2 is unconfigured for
        // this contact and does not restrict. It must NOT silently inherit the
        // inactive role's narrower grant.
        Assert.Equal(TileAccessReason.NotConfigured, Reason(result, Configurator));
        Assert.True(Granted(result, Configurator));
    }

    [Fact]
    public async Task Per_client_override_beats_the_global_default()
    {
        await using var ctx = NewContext(nameof(Per_client_override_beats_the_global_default));
        Seed(ctx, roleIdsForContact: [DispatcherRole]);
        // Global default grants Configurator; this client's override revokes it.
        ctx.RolePermissions.Add(Grant(DispatcherRole, Configurator));
        ctx.RolePermissions.Add(Grant(DispatcherRole, Configurator, clientId: ClientId, level: 0));
        ctx.RolePermissions.Add(Grant(DispatcherRole, DespatchWeb));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await Resolve(ctx, enabled: [Configurator, DespatchWeb]);

        Assert.False(Granted(result, Configurator));
        Assert.Equal(TileAccessReason.RoleLacksTile, Reason(result, Configurator));
        Assert.True(Granted(result, DespatchWeb));
    }

    [Fact]
    public async Task Another_clients_override_is_not_applied()
    {
        await using var ctx = NewContext(nameof(Another_clients_override_is_not_applied));
        Seed(ctx, roleIdsForContact: [DispatcherRole]);
        ctx.RolePermissions.Add(Grant(DispatcherRole, Configurator));
        // An override belonging to a DIFFERENT client must not leak across.
        ctx.RolePermissions.Add(Grant(DispatcherRole, Configurator, clientId: 999, level: 0));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await Resolve(ctx, enabled: [Configurator]);

        Assert.True(Granted(result, Configurator));
    }

    [Fact]
    public async Task Df_admin_bypasses_gate_two()
    {
        await using var ctx = NewContext(nameof(Df_admin_bypasses_gate_two));
        Seed(ctx, roleIdsForContact: [DispatcherRole], clientTypeId: 5);
        // A grant that would otherwise restrict them to DespatchWeb only.
        ctx.RolePermissions.Add(Grant(DispatcherRole, DespatchWeb));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await Resolve(ctx, enabled: [Configurator, DespatchWeb]);

        Assert.All(result, d => Assert.True(d.Granted));
        Assert.Equal(TileAccessReason.DfAdminBypass, Reason(result, Configurator));
        // Note the bypass is total: even a tile gate 1 did not enable is granted,
        // matching IFeatureResolver's DF-Admin union behaviour.
        Assert.True(Granted(result, Tracking));
    }

    [Fact]
    public async Task Legacy_single_role_is_used_when_no_stacked_roles_exist()
    {
        await using var ctx = NewContext(nameof(Legacy_single_role_is_used_when_no_stacked_roles_exist));
        Seed(ctx, roleIdsForContact: [], legacyContactRoleId: DispatcherRole);
        ctx.RolePermissions.Add(Grant(DispatcherRole, DespatchWeb));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await Resolve(ctx, enabled: [Configurator, DespatchWeb]);

        Assert.True(Granted(result, DespatchWeb));
        Assert.False(Granted(result, Configurator));
    }

    [Fact]
    public async Task Allowed_false_rows_predating_the_backfill_do_not_grant()
    {
        await using var ctx = NewContext(nameof(Allowed_false_rows_predating_the_backfill_do_not_grant));
        Seed(ctx, roleIdsForContact: [DispatcherRole]);
        ctx.RolePermissions.Add(Grant(DispatcherRole, DespatchWeb));
        // AccessLevel NULL + Allowed false = an explicit legacy revoke.
        ctx.RolePermissions.Add(new RolePermission
        {
            RolePermissionId = 900,
            ContactRoleId = DispatcherRole,
            PermissionKey = Configurator,
            Allowed = false,
            AccessLevel = null,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await Resolve(ctx, enabled: [Configurator, DespatchWeb]);

        Assert.False(Granted(result, Configurator));
        Assert.True(Granted(result, DespatchWeb));
    }

    // ---- helpers ----------------------------------------------------------

    private static async Task<IReadOnlyList<TileAccess>> Resolve(
        DynamicDespatchDbContext ctx, string[] enabled)
        => await new TileAccessResolver(ctx).ResolveAsync(
            ContactId, ClientId, new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase));

    private static bool Granted(IReadOnlyList<TileAccess> r, string key)
        => r.Single(d => d.TileKey == key).Granted;

    private static TileAccessReason Reason(IReadOnlyList<TileAccess> r, string key)
        => r.Single(d => d.TileKey == key).Reason;

    private static RolePermission Grant(
        int roleId, string tileKey, int? clientId = null, byte level = 1)
        => new()
        {
            // Deterministic id so a test can seed a global and an override for
            // the same (role, tile) without colliding.
            RolePermissionId = roleId * 1000 + tileKey.GetHashCode() % 100 + (clientId ?? 0),
            ContactRoleId = roleId,
            PermissionKey = tileKey,
            Allowed = level >= 1,
            AccessLevel = level,
            ClientId = clientId,
        };

    private static void Seed(
        DynamicDespatchDbContext ctx,
        int[] roleIdsForContact,
        int[]? inactiveRoleIds = null,
        int? legacyContactRoleId = null,
        int clientTypeId = 4)
    {
        inactiveRoleIds ??= [];

        foreach (var key in new[] { Configurator, DespatchWeb, Tracking })
        {
            ctx.Features.Add(NewFeature(key, "HubTile"));
        }

        // A non-tile feature, to prove the resolver only ever decides tiles.
        ctx.Features.Add(NewFeature("cfg-fleet", "MenuItem"));

        foreach (var roleId in new[] { AdminRole, DispatcherRole })
        {
            ctx.TblContactRoles.Add(new TblContactRole
            {
                ContactRoleId = roleId,
                IsActive = !inactiveRoleIds.Contains(roleId),
            });
        }

        foreach (var roleId in roleIdsForContact)
        {
            ctx.TblContactContactRoles.Add(new TblContactContactRole
            {
                ClientContactId = ContactId,
                ContactRoleId = roleId,
            });
        }

        ctx.TucClientContacts.Add(new TucClientContact
        {
            UcctId = ContactId,
            UcctClientId = ClientId,
            ContactRoleId = legacyContactRoleId,
            // Required by the scaffold; irrelevant to the rules under test.
            CreatedBy = Stub, LastModifiedBy = Stub,
        });

        ctx.TucClients.Add(new TucClient
        {
            UcclId = ClientId,
            ClientTypeId = clientTypeId,
            CreatedBy = Stub, LastModifiedBy = Stub, Smsname = Stub,
            UcclCode = Stub, UcclLegalName = Stub, UcclName = Stub,
        });
    }

    // The scaffolded entities carry required audit/display columns that none of
    // these rules touch. Filled with a stub rather than nulls so the in-memory
    // provider will accept them.
    private const string Stub = "test";

    private static Feature NewFeature(string key, string category)
        => new() { FeatureKey = key, DisplayName = key, Description = Stub, Category = category };

    private static DynamicDespatchDbContext NewContext(string name)
    {
        var options = new DbContextOptionsBuilder<DespatchContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        // The connection-string manager is never consulted: OnConfiguring
        // short-circuits when the options are already configured.
        return new DynamicDespatchDbContext(options, Substitute.For<IConnectionStringManager>());
    }
}
