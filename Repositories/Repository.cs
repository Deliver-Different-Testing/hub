using Hub.Interfaces;
using Hub.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Hub.Repositories;

public sealed class Repository(
    DynamicDespatchDbContext context,
    ITenantService tenantService) : IDespatchRepository
{
    public async Task<TucClientContact?> FetchUserByUsernameAsync(string email)
    {
        try
        {
            Log.Debug("Attempting to fetch user with email: {Email}", email);

            return await context.TucClientContacts
                .AsNoTracking()
                .Include(c => c.UcctClient)
                .Where(x => x.Active && x.UserName == email)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching user with email: {Email}", email);
            throw;
        }
    }

    public async Task<string> FetchSubAccountsAsync(int clientId)
    {
        var subAccounts = await context.TucClients
            .AsNoTracking()
            .Where(x => x.UcclGroupId == clientId)
            .Select(y => y.UcclId)
            .ToListAsync();
        return string.Join(",", subAccounts);
    }

    public async Task<List<RVW_stpValidateInternetPermissionsResult>> GetDespatchWebInternetPermissionsAsync(int contactId)
    {
        var data = await context.Procedures.RVW_stpValidateInternetPermissionsAsync(contactId);
        return data;
    }

    public async Task InitiatePasswordResetAsync(int contactId, string recoveryEmail, string replyEmail, string link) =>
        await context.Procedures.NET_stpContact_ResetPasswordAsync(contactId, recoveryEmail, replyEmail, link);

    public async Task UpdateUserAccessedAsync(int id, bool rememberMe, int tenantId)
    {
        var tenantTime = await tenantService.GetCurrentTenantTimeAsync(tenantId);
        await context.TucClientContacts
            .Where(x => x.UcctId == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.LastAccessed, tenantTime)
                .SetProperty(x => x.HasEmail, x => x.WhenEmailValidated == null || x.HasEmail)
                .SetProperty(x => x.ValidatedEmail, x => x.WhenEmailValidated == null || x.ValidatedEmail)
                .SetProperty(x => x.WhenEmailValidatedSent,
                    x => x.WhenEmailValidated == null ? tenantTime : x.WhenEmailValidatedSent)
                .SetProperty(x => x.WhenEmailValidated, x => x.WhenEmailValidated ?? tenantTime)
                .SetProperty(x => x.AllowCookieLogin, rememberMe));
    }

    public async Task<int?> ValidateCourierByEmailAsync(string email)
    {
        try
        {
            Log.Debug("Validating courier with email: {Email}", email);

            var courierId = await context.TucCouriers
                .AsNoTracking()
                .Where(x => x.Active && x.UccrEmail != null && x.UccrEmail.Trim() == email)
                .Select(x => (int?)x.UccrId)
                .FirstOrDefaultAsync();

            if (courierId.HasValue)
            {
                Log.Debug("Found courier with ID: {CourierId}", courierId.Value);
                return courierId;
            }

            Log.Warning("No active courier found with email: {Email}", email);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error validating courier with email: {Email}", email);
            return null;
        }
    }

    public async Task<int?> GetAccountsModeAsync()
    {
        try
        {
            var accountsMode = await context.TblSettings
                .AsNoTracking()
                .Select(s => s.AccountsMode)
                .FirstOrDefaultAsync();

            Log.Debug("AccountsMode: {AccountsMode}", accountsMode?.ToString() ?? "NULL");
            return accountsMode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching AccountsMode from TblSettings");
            return null;
        }
    }

    public async Task<bool> IsAfterHoursAuthorizedAsync(int courierId)
    {
        try
        {
            // 2026-01-20 New logic from George - just existence of courier qualifies for auth.
            var isAuthorized = await context.TblAfterhoursCouriers
                .AsNoTracking()
                .AnyAsync(ah => ah.CourierId == courierId);

            return isAuthorized;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking after-hours authorization for courier {CourierId}", courierId);
            return false;
        }
    }
}