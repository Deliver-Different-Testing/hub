using System.Security.Claims;
using Hub.Features.ApiKeys;
using Hub.Interfaces;
using Hub.Models;
using Hub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Hub.Tests.Features.ApiKeys;

// Who may see Settings and generate a Hub API key (spec GARRY-HUB-API-KEY-TENANT-ADMIN-ONLY, 13 Nov 2026).
// One test per row of the spec's table, then the manage-api-keys verb rules for tenant users.
public class ApiKeyAccessResolverTests
{
    private const int ContactId = 100;
    private const int ClientId = 200;
    private const int OtherClientId = 201;
    private const int AdminRole = 1;
    private const int OpsRole = 2;
    private const string Verb = ApiKeyAccessResolver.ManageApiKeys;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- the table ----

    [Theory]
    [InlineData("2")]
    [InlineData("")]
    public async Task A_customer_keeps_settings_and_the_key(string clientType)
    {
        await using var ctx = NewContext(nameof(A_customer_keeps_settings_and_the_key) + clientType);

        Assert.Equal(ApiKeyAccess.Full, await Resolve(ctx, clientType));
    }

    [Fact]
    public async Task A_tenant_admin_holding_the_verb_gets_settings_and_the_key()
    {
        await using var ctx = NewContext(nameof(A_tenant_admin_holding_the_verb_gets_settings_and_the_key));
        Seed(ctx, AdminRole);
        Set(ctx, AdminRole, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ApiKeyAccess.Full, await Resolve(ctx, "4"));
    }

    [Fact]
    public async Task Tenant_staff_without_the_verb_get_neither()
    {
        await using var ctx = NewContext(nameof(Tenant_staff_without_the_verb_get_neither));
        Seed(ctx, OpsRole);
        Set(ctx, OpsRole, 0);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ApiKeyAccess.None, await Resolve(ctx, "4"));
    }

    [Fact]
    public async Task A_df_admin_gets_settings_and_the_key_without_any_role_row()
    {
        await using var ctx = NewContext(nameof(A_df_admin_gets_settings_and_the_key_without_any_role_row));

        Assert.Equal(ApiKeyAccess.Full, await Resolve(ctx, "5"));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("6")]
    [InlineData("1")]
    [InlineData("junk")]
    public async Task Network_partners_and_any_other_client_type_get_neither(string clientType)
    {
        await using var ctx = NewContext(nameof(Network_partners_and_any_other_client_type_get_neither) + clientType);
        Seed(ctx, AdminRole);
        Set(ctx, AdminRole, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ApiKeyAccess.None, await Resolve(ctx, clientType));
    }

    [Fact]
    public async Task A_courier_gets_neither()
    {
        await using var ctx = NewContext(nameof(A_courier_gets_neither));

        Assert.Equal(ApiKeyAccess.None, await Resolve(ctx, "", isCourier: true));
    }

    // ---- the verb, for tenant users ----

    [Fact]
    public async Task No_row_on_the_verb_is_no_even_with_no_other_rows()
    {
        await using var ctx = NewContext(nameof(No_row_on_the_verb_is_no_even_with_no_other_rows));
        Seed(ctx, AdminRole);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ApiKeyAccess.None, await Resolve(ctx, "4"));
    }

    [Fact]
    public async Task Stacked_roles_union_so_one_admin_role_is_enough()
    {
        await using var ctx = NewContext(nameof(Stacked_roles_union_so_one_admin_role_is_enough));
        Seed(ctx, OpsRole, AdminRole);
        Set(ctx, OpsRole, 0);
        Set(ctx, AdminRole, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ApiKeyAccess.Full, await Resolve(ctx, "4"));
    }

    [Fact]
    public async Task An_inactive_role_counts_for_nothing()
    {
        await using var ctx = NewContext(nameof(An_inactive_role_counts_for_nothing));
        Seed(ctx, [AdminRole], inactive: [AdminRole]);
        Set(ctx, AdminRole, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ApiKeyAccess.None, await Resolve(ctx, "4"));
    }

    [Fact]
    public async Task The_legacy_single_role_is_used_when_no_roles_are_stacked()
    {
        await using var ctx = NewContext(nameof(The_legacy_single_role_is_used_when_no_roles_are_stacked));
        Seed(ctx, [], legacyRole: AdminRole);
        Set(ctx, AdminRole, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ApiKeyAccess.Full, await Resolve(ctx, "4"));
    }

    [Fact]
    public async Task A_client_override_beats_the_global_row()
    {
        await using var ctx = NewContext(nameof(A_client_override_beats_the_global_row));
        Seed(ctx, AdminRole);
        Set(ctx, AdminRole, 1);
        Set(ctx, AdminRole, 0, ClientId);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ApiKeyAccess.None, await Resolve(ctx, "4"));
    }

    [Fact]
    public async Task Another_clients_override_is_ignored()
    {
        await using var ctx = NewContext(nameof(Another_clients_override_is_ignored));
        Seed(ctx, AdminRole);
        Set(ctx, AdminRole, 0);
        Set(ctx, AdminRole, 1, OtherClientId);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ApiKeyAccess.None, await Resolve(ctx, "4"));
    }

    [Fact]
    public async Task A_duplicate_row_can_only_deny()
    {
        await using var ctx = NewContext(nameof(A_duplicate_row_can_only_deny));
        Seed(ctx, AdminRole);
        Set(ctx, AdminRole, 1);
        ctx.RolePermissions.Add(Row(AdminRole, 0, null));
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ApiKeyAccess.None, await Resolve(ctx, "4"));
    }

    [Fact]
    public async Task A_tenant_user_with_no_contact_id_gets_neither()
    {
        await using var ctx = NewContext(nameof(A_tenant_user_with_no_contact_id_gets_neither));
        Seed(ctx, AdminRole);
        Set(ctx, AdminRole, 1);
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(ApiKeyAccess.None, await Resolve(ctx, "4", contactId: "0"));
    }

    // ---- helpers ----

    private static Task<ApiKeyAccess> Resolve(
        DynamicDespatchDbContext ctx, string clientType, bool isCourier = false, string contactId = "100")
    {
        var user = ClaimsPrincipalFactory.Create(
            contactId: contactId, clientId: ClientId.ToString(), clientTypeId: clientType, isCourier: isCourier);
        return new ApiKeyAccessResolver(ctx, Substitute.For<IConnectionStringManager>()).ResolveAsync(user, Ct);
    }

    private static void Seed(DynamicDespatchDbContext ctx, params int[] roles) => Seed(ctx, roles, null, null);

    private static void Seed(DynamicDespatchDbContext ctx, int[] roles, int[]? inactive = null, int? legacyRole = null)
    {
        foreach (var roleId in new[] { AdminRole, OpsRole })
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
    }

    private static int nextRowId;

    private static void Set(DynamicDespatchDbContext ctx, int roleId, byte level, int? clientId = null) =>
        ctx.RolePermissions.Add(Row(roleId, level, clientId));

    private static RolePermission Row(int roleId, byte level, int? clientId) => new()
    {
        RolePermissionId = Interlocked.Increment(ref nextRowId),
        ContactRoleId = roleId,
        PermissionKey = Verb,
        Allowed = level >= 1,
        AccessLevel = level,
        ClientId = clientId
    };

    private const string Stub = "test";

    private static DynamicDespatchDbContext NewContext(string name)
    {
        var options = new DbContextOptionsBuilder<DespatchContext>()
            .UseInMemoryDatabase(nameof(ApiKeyAccessResolverTests) + name)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new DynamicDespatchDbContext(options, Substitute.For<IConnectionStringManager>());
    }
}
