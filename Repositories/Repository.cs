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

    public async Task<TucClientContact?> FetchAgentContactAsync(int contactId) =>
        await context.TucClientContacts
            .AsNoTracking()
            .Include(c => c.UcctClient)
            .FirstOrDefaultAsync(c => c.UcctId == contactId);

    // Missing-table guard (B1): until the B0 migration reaches a tenant DB, tblContactBranch / tblBranch do not exist and
    // the query fails with SQL error 208. That must not fail login, so it means "no rows" = no restriction (correct for
    // such a tenant, since nobody can have branches there yet). No caching: a not-yet-migrated tenant pays one failed
    // round trip per login / pass refresh, and the claim starts working the moment the migration lands, without a Hub
    // restart. Any OTHER error is rethrown on purpose: emitting an empty claim on a timeout would silently widen a
    // branch-limited user to every branch (fail open), so login fails instead (fail closed).
    public async Task<int[]> GetContactBranchIdsAsync(int contactId)
    {
        try
        {
            return await (
                    from cb in context.TblContactBranches.AsNoTracking()
                    join b in context.TblBranches.AsNoTracking() on cb.BranchId equals b.BranchId
                    where cb.ClientContactId == contactId && b.Active
                    orderby cb.BranchId
                    select cb.BranchId)
                .ToArrayAsync(); // PK (ClientContactId, BranchId) means no duplicates
        }
        catch (Exception ex) when (Hub.Shared.BranchIdsClaim.IsMissingTable(ex))
        {
            Log.Debug("tblContactBranch / tblBranch not in this tenant DB yet; contact {ContactId} gets no branch restriction",
                contactId);
            return [];
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