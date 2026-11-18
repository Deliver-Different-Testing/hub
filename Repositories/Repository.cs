using Hub.Interfaces;
using Hub.Models;
using Hub.Services;
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

            var contact = await context.TucClientContacts
                .AsNoTracking()
                .Include(c => c.UcctClient)
                .Where(x => x.Active && x.UserName == email)
                .FirstOrDefaultAsync();

            return await WithBranchesAsync(contact);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching user with email: {Email}", email);
            throw;
        }
    }

    public async Task<TucClientContact?> FetchAgentContactAsync(int contactId) =>
        await WithBranchesAsync(await context.TucClientContacts
            .AsNoTracking()
            .Include(c => c.UcctClient)
            .FirstOrDefaultAsync(c => c.UcctId == contactId));

    // Branches programme (B1): the contact's branch scope (tblContactBranch) for the BranchIds claim. No rows = no scope =
    // sees every branch. A tenant that has not had the branch migration yet has no such table (SQL error 208), which
    // also means nobody there is scoped, so login carries on with an empty list. Any other failure is thrown: guessing
    // "no scope" on an error would quietly widen what someone can see.
    // Only tenant staff are ever limited, so the table is not read at all for anyone else (customers, network partners,
    // DF Admin): their claim is empty whatever rows exist, and their sign-in must not depend on that table being readable.
    // Staff still fail closed: any error other than a missing table is thrown.
    private async Task<TucClientContact?> WithBranchesAsync(TucClientContact? contact)
    {
        if (contact is null) return null;

        if (BranchClaim.IsLimitable(contact.UcctClient?.ClientTypeId))
        {
            contact.BranchIds = await ReadBranchIdsAsync(contact.UcctId);
        }

        return contact;
    }

    private async Task<List<int>> ReadBranchIdsAsync(int contactId)
    {
        try
        {
            return await context.TblContactBranches
                .AsNoTracking()
                .Where(b => b.ClientContactId == contactId)
                .Select(b => b.BranchId)
                .OrderBy(b => b)
                .ToListAsync();
        }
        catch (Exception ex) when (TenantSqlErrors.IsMissingTable(ex))
        {
            Log.Debug("tblContactBranch does not exist in this tenant yet; no branch scope for contact {ContactId}", contactId);
            return [];
        }
    }

    // The refresh path for an already signed-in person: just their current branch scope. Null when the contact is gone or
    // inactive, so a removed person is never turned into "no scope" (every branch) by their rows disappearing.
    public async Task<IReadOnlyList<int>?> GetContactBranchIdsAsync(int contactId)
    {
        var active = await context.TucClientContacts.AsNoTracking().AnyAsync(c => c.UcctId == contactId && c.Active);
        return active ? await ReadBranchIdsAsync(contactId) : null;
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