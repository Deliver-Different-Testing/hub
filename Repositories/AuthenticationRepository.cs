using Hub.Interfaces;
using Hub.Models.Master;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Hub.Services;
using Hub.ViewModels;

namespace Hub.Repositories;

public sealed class AuthenticationRepository(MasterContext context) : IAuthenticationRepository
{
    public async Task<User?> GetUserByEmailAsync(string email, bool? isCourier = null)
    {
        var query = context.Users
            .Include(u => u.CurrentTenant)
            .Where(u => u.Email == email);

        // Filter by IsCourier if specified
        if (isCourier.HasValue)
        {
            // Looking for courier: IsCourier must be true
            query = isCourier.Value
                ? query.Where(u => u.IsCourier == true)
                : query.Where(u => u.IsCourier == false || u.IsCourier == null);
        }

        return await query.FirstOrDefaultAsync();
    }

    /// <summary>
    /// The user behind a Shopify merchant sign-in, or null.
    /// <para>
    /// Separate from <see cref="GetUserByEmailAsync"/>, which serves a caller that has already been
    /// told which kind of account to look for - the portal makes the user say so with a toggle. The
    /// Shopify app has one login box and no toggle, so this decides, and it decides <em>merchant</em>:
    /// the account signing in is a dispatch client contact, matched afterwards against
    /// <c>tucClientContact.UserName</c> in the courier's own database. A courier operator is a
    /// different population in a different table, so their row is not a fallback here - letting one
    /// through would mint a merchant link ticket for a courier's own staff account.
    /// </para>
    /// <para>
    /// <c>OrderBy(UserId)</c> is not tidiness. <c>IX_User_Email</c> is unique on (Email, IsCourier)
    /// and treats NULL and 0 as distinct keys, so one email can hold two non-courier rows; an
    /// unordered <c>FirstOrDefault</c> would authenticate whichever the plan reached first, which is
    /// a different password on different days.
    /// </para>
    /// <para>
    /// Tracked, because a legacy hash is upgraded in place on success. No
    /// <c>Include(u =&gt; u.CurrentTenant)</c>: it would pull <c>Tenant.DBConnection</c> into a SELECT
    /// on the one path that must never read it, and the tenant comes from TenantUser here anyway.
    /// </para>
    /// </summary>
    public async Task<User?> GetShopifyMerchantUserAsync(string email) =>
        await context.Users
            .Where(u => u.Email == email && (u.IsCourier == false || u.IsCourier == null))
            .OrderBy(u => u.UserId)
            .FirstOrDefaultAsync();

    /// <summary>
    /// The couriers a merchant may connect a store to: the tenants they belong to, narrowed to those
    /// with Shopify switched on.
    /// <para>
    /// An inner join to <c>ShopifyTenantHost</c> rather than a flag on a full list. A tenant with no
    /// host row has nowhere to hand a store to, and naming it anyway would tell the public front
    /// door which other couriers this merchant deals with.
    /// </para>
    /// <para>
    /// The host is returned as stored. Whether it is <em>usable</em> is decided above this, by the
    /// same check the front door applies, because <c>Uri.IsWellFormedUriString</c> does not translate
    /// to SQL.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ShopifySignInTenant>> GetShopifyTenantsForUserAsync(int userId) =>
        await context.TenantUsers.AsNoTracking()
            .Where(tu => tu.UserId == userId)
            .Join(context.ShopifyTenantHosts,
                tu => tu.TenantId,
                host => host.TenantId,
                (tu, host) => new ShopifySignInTenant
                {
                    TenantId = tu.TenantId,
                    Code = tu.Tenant.Code,
                    Name = tu.Tenant.Name,
                    Host = host.IntegrationManagerUrl
                })
            .OrderBy(t => t.Name)
            .ToListAsync();

    /// <summary>
    /// Records where a tenant's Integration Manager lives, creating the row or updating it. False if
    /// there is no such tenant.
    /// <para>
    /// The row's existence is the on/off switch for Shopify on that courier, so this is also how a
    /// courier is switched on. Nothing wrote it before - the comments that said pairing-code issuance
    /// did were describing an intention, not code - which left the table empty and every merchant
    /// with no courier to connect to.
    /// </para>
    /// </summary>
    public async Task<bool> UpsertShopifyTenantHostAsync(int tenantId, string integrationManagerUrl)
    {
        if (!await context.Tenants.AnyAsync(t => t.TenantId == tenantId))
        {
            return false;
        }

        var host = await context.ShopifyTenantHosts.FirstOrDefaultAsync(h => h.TenantId == tenantId);

        if (host == null)
        {
            context.ShopifyTenantHosts.Add(new ShopifyTenantHost
            {
                TenantId = tenantId,
                IntegrationManagerUrl = integrationManagerUrl,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            host.IntegrationManagerUrl = integrationManagerUrl;
            host.UpdatedAtUtc = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyList<TenantUserSettingViewModel>> GetUserSettingsAsync(int tenantId, int userId) =>
        await context.TenantUserSettings
            .AsNoTracking()
            .Where(tus => tus.TenantId == tenantId && tus.UserId == userId)
            .Select(tus => new TenantUserSettingViewModel
            {
                Id = tus.TenantUserSettingId,
                Name = tus.SettingName,
                Value = tus.SettingValue
            })
            .ToListAsync();

    public async Task SaveUserSettingAsync(TenantUserSettingViewModel viewModel, int tenantId, int userId)
    {
        // Check if setting already exists
        var existingSetting = await context.TenantUserSettings
            .FirstOrDefaultAsync(tus =>
                tus.TenantId == tenantId &&
                tus.UserId == userId &&
                tus.SettingName == viewModel.Name);

        if (existingSetting != null)
        {
            // Update existing setting
            existingSetting.SettingValue = viewModel.Value;
        }
        else
        {
            // Create new setting
            var newSetting = new TenantUserSetting
            {
                TenantId = tenantId,
                UserId = userId,
                SettingName = viewModel.Name,
                SettingValue = viewModel.Value
            };

            await context.TenantUserSettings.AddAsync(newSetting);
        }

        await context.SaveChangesAsync();
    }

    public async Task SaveAsync() => await context.SaveChangesAsync();

    public async Task<User?> GetUserByIdAsync(int id) =>
        await context.Users
            .AsNoTracking()
            .Include(u => u.CurrentTenant)
            .FirstOrDefaultAsync(u => u.UserId == id);

    public async Task<User?> GetUserByResetKeyAsync(string resetKey) =>
        await context.Users
            .Include(u => u.CurrentTenant)
            .FirstOrDefaultAsync(u => u.ResetKey == resetKey);

    public async Task<IReadOnlyList<Tenant>> GetTenantsByUserIdAsync(int userId) =>
        await context.TenantUsers.AsNoTracking().Where(tu => tu.UserId == userId).Select(tu => tu.Tenant)
            .Distinct()
            .ToListAsync();

    public async Task<string?> GetTenantTimeZoneAsync(int tenantId) =>
        await context.Tenants
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.TimeZone)
            .FirstOrDefaultAsync();

    public async Task<string?> GetTenantConnectionStringAsync(int tenantId) =>
        await context.Tenants
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.Dbconnection)
            .FirstOrDefaultAsync();

    public async Task<ShopifyShopTenantResponse?> GetTenantByShopifyShopAsync(string shop)
    {
        var normalised = Normalise(shop);

        return await context.ShopifyShopTenants
            .AsNoTracking()
            .Where(m => m.Shop == normalised)
            .Select(m => new ShopifyShopTenantResponse
            {
                Shop = m.Shop,
                ShopName = m.ShopName,
                TenantId = m.TenantId,
                TenantCode = m.Tenant.Code
            })
            .FirstOrDefaultAsync();
    }

    public async Task<ShopifyShopMappingResult> MapShopifyShopAsync(string shop, int tenantId)
    {
        var normalised = Normalise(shop);

        if (!await context.Tenants.AsNoTracking().AnyAsync(t => t.TenantId == tenantId))
        {
            Log.Warning("Refused to map Shopify shop {Shop}: no tenant {TenantId}", normalised, tenantId);
            return ShopifyShopMappingResult.TenantNotFound;
        }

        var existing = await context.ShopifyShopTenants
            .AsNoTracking()
            .Where(m => m.Shop == normalised)
            .Select(m => (int?)m.TenantId)
            .FirstOrDefaultAsync();

        if (existing == tenantId)
        {
            return ShopifyShopMappingResult.AlreadyMapped;
        }

        if (existing != null)
        {
            Log.Error("Shopify shop {Shop} is mapped to tenant {ExistingTenantId}; refused to re-point it to {TenantId}",
                normalised, existing, tenantId);
            return ShopifyShopMappingResult.ConflictsWithAnotherTenant;
        }

        context.ShopifyShopTenants.Add(new ShopifyShopTenant
        {
            // Assigned here rather than by the store - MasterContext configures ValueGeneratedNever.
            Id = Guid.NewGuid(),
            Shop = normalised,
            TenantId = tenantId
        });
        await context.SaveChangesAsync();

        Log.Information("Mapped Shopify shop {Shop} to tenant {TenantId}", normalised, tenantId);
        return ShopifyShopMappingResult.Mapped;
    }

    public async Task<bool> UnmapShopifyShopAsync(string shop, int tenantId)
    {
        var normalised = Normalise(shop);

        // Matched on both, not just the shop. A delete keyed on the shop alone would let any caller
        // detach any store, and the caller here is a shared deployment serving every tenant.
        var mapping = await context.ShopifyShopTenants
            .FirstOrDefaultAsync(m => m.Shop == normalised && m.TenantId == tenantId);

        if (mapping == null)
        {
            return false;
        }

        context.ShopifyShopTenants.Remove(mapping);
        await context.SaveChangesAsync();

        Log.Information("Unmapped Shopify shop {Shop} from tenant {TenantId}", normalised, tenantId);
        return true;
    }

    /// <summary>
    /// Shop domains are case-insensitive and arrive with whatever casing the caller used. The
    /// column is uniquely constrained - one tenant per shop - so the value has to be settled here
    /// rather than relying on the database collation.
    /// </summary>
    private static string Normalise(string shop) => shop.Trim().ToLowerInvariant();

    public async Task<bool> IsUserAssociatedWithTenantAsync(int userId, int tenantId) =>
        await context.TenantUsers
            .AnyAsync(tu => tu.UserId == userId && tu.TenantId == tenantId);

    public async Task<bool> UpdateCurrentTenantIdAsync(int userId, int tenantId)
    {
        var user = await context.Users.FindAsync(userId);

        if (user == null)
        {
            return false;
        }

        // Check if the user is associated with the tenant
        if (!await IsUserAssociatedWithTenantAsync(userId, tenantId))
        {
            return false;
        }

        user.CurrentTenantId = tenantId;

        try
        {
            await context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex)
        {
            Log.Error(ex, "Error updating Current Tenant. User {UserId} Tenant {TenantId}", userId, tenantId);
            return false;
        }
    }

    public async Task<User?> CreateNpUserAsync(string email, int currentTenantId) =>
        await CreateUserAsync(email, currentTenantId, isNetworkPartner: true);

    public async Task<User?> CreateUserAsync(string email, int currentTenantId, bool isNetworkPartner)
    {
        // 409-shape: don't insert if email already exists in Master.User.
        // Caller surfaces this to the operator as "user already exists in
        // Hub — manual remediation needed". Re-invite-existing is a
        // separate slice (idempotency Open Question #3 in the brief).
        var existing = await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == email);

        if (existing)
        {
            return null;
        }

        var user = new User
        {
            Email = email,
            // Password / Salt are empty until the invitee sets their
            // password via the reset-key flow. IsLegacyHash=false marks
            // this row as PBKDF2/SHA256-ready when the password gets set.
            Password = string.Empty,
            Salt = string.Empty,
            IsLegacyHash = false,
            ResetKey = Guid.NewGuid().ToString(),
            CurrentTenantId = currentTenantId,
            // Tenant users (configurator Team page) and Network Partners share
            // this provisioning path; only the IsNetworkPartner data-scope flag
            // differs. Neither is a courier.
            IsNetworkPartner = isNetworkPartner,
            IsCourier = false
        };

        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();
        return user;
    }

    public async Task<bool> EmailExistsAsync(string email) =>
        await context.Users.AsNoTracking().AnyAsync(u => u.Email == email);
}
