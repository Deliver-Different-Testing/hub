using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hub.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Hub.Repositories;

public partial class Repository(DynamicDespatchDbContext context)
{
    private void LogConnectionDetails()
    {
        try
        {
            var connection = context.Database.GetDbConnection();
            var maskedConnectionString = MaskSensitiveInfo(connection.ConnectionString);

            Log.Information("Current Connection Details:");
            Log.Information("Data Source: {ConnectionDataSource}", connection.DataSource);
            Log.Information("Database: {ConnectionDatabase}", connection.Database);
            Log.Information("Masked Connection String: {MaskedConnectionString}", maskedConnectionString);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error logging connection details");
        }
    }

    private static string MaskSensitiveInfo(string connectionString)
    {
        // Mask password
        var maskedString = PasswordRegex().Replace(connectionString, "$1=********");

        // Mask user id if present
        maskedString = UserNameRegex().Replace(maskedString, "$1=********");

        return maskedString;
    }

    public async Task<TucClientContact> FetchUserByUsername(string email)
    {
        LogConnectionDetails();
        try
        {
            Log.Information("Attempting to fetch user with email: {Email}", email);

            return await context.TucClientContacts.Where(x => x.Active && x.UserName == email)
                .Include(c => c.UcctClient)
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
        var subAccounts = await context.TucClients.Where(x => x.UcclGroupId == clientId).Select(y => y.UcclId)
            .ToListAsync();
        return string.Join(",", subAccounts.Select(n => n.ToString()).ToArray());
    }

    public async Task<List<RVW_stpValidateInternetPermissionsResult>> GetDespatchWebInternetPermissions(int contactId)
    {
        var data = await context.Procedures.RVW_stpValidateInternetPermissionsAsync(contactId);
        return data;
    }


    public async Task InitiatePasswordReset(int contactId, string recoveryEmail, string replyEmail, string link) =>
        await context.Procedures.NET_stpContact_ResetPasswordAsync(contactId, recoveryEmail, replyEmail, link);

    public void UpdateUserAccessed(int id, bool rememberMe)
    {
        var contact = context.TucClientContacts.FirstOrDefault(x => x.UcctId == id);
        if (contact == null) return;
        contact.LastAccessed = DateTime.Now;
        contact.HasEmail = contact.WhenEmailValidated == null || contact.HasEmail;
        contact.ValidatedEmail = contact.WhenEmailValidated == null || contact.ValidatedEmail;
        contact.WhenEmailValidated ??= DateTime.Now;
        contact.WhenEmailValidatedSent = contact.WhenEmailValidated == null
            ? DateTime.Now
            : contact.WhenEmailValidatedSent;
        contact.AllowCookieLogin = rememberMe;
        context.SaveChangesAsync();
    }

    public async Task<int?> ValidateCourierByEmail(string email)
    {
        LogConnectionDetails();
        try
        {
            Log.Information("Attempting to validate courier with email: {Email}", email);

            // Get the tucCourier record with the given email
            var courier = await context.TucCouriers
                .FirstOrDefaultAsync(x => x.Active && x.UccrEmail != null && x.UccrEmail.Trim() == email);

            if (courier != null)
            {
                Log.Information("Found courier with ID: {CourierUccrId}", courier.UccrId);
                return courier.UccrId;
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
        LogConnectionDetails();
        try
        {
            Log.Information("Fetching AccountsMode from TblSettings");

            var settings = await context.TblSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                Log.Warning("TblSettings record not found");
                return null;
            }

            Log.Information("AccountsMode: {ToString}", settings.AccountsMode?.ToString() ?? "NULL");
            return settings.AccountsMode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching AccountsMode from TblSettings");
            return null;
        }
    }

    public async Task<bool> IsAfterHoursAuthorized(int courierId, int dayOfWeek)
    {
        LogConnectionDetails();
        try
        {
            Log.Information("Checking after-hours authorization for courier {CourierId} on day {DayOfWeek}", courierId,
                dayOfWeek);

            // Check if a tblAfterHoursCourier record exists for this courier.
            // 2026-01-20 New logic from George - ignore day/time. Just existence of courier qualifies for auth.
            var isAuthorized = await context.TblAfterhoursCouriers
                .AnyAsync(ah => ah.CourierId == courierId);

            return isAuthorized;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking after-hours authorization for courier {CourierId} on day {DayOfWeek}",
                courierId, dayOfWeek);
            return false;
        }
    }

    [GeneratedRegex("(Password|Pwd)=[^;]*", RegexOptions.IgnoreCase, "en-NZ")]
    private static partial Regex PasswordRegex();

    [GeneratedRegex("(User ID|Uid)=[^;]*", RegexOptions.IgnoreCase, "en-NZ")]
    private static partial Regex UserNameRegex();
}