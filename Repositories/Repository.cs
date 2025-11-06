using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hub.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Hub.Repositories
{
    public class Repository(DynamicDespatchDbContext context)
    {
        
        public async Task<RVW_stpValidateClientResult> GetClient(int clientId)
        {
            LogConnectionDetails();
            var data = await context.Procedures.RVW_stpValidateClientAsync(clientId);
            return data.FirstOrDefault();
        }

        public async Task<RVW_stpValidateContactResult> GetContact(int contactId)
        {
            LogConnectionDetails();
            var data = await context.Procedures.RVW_stpValidateContactAsync(contactId);


            return data.FirstOrDefault();
        } 
        
        public async Task SaveAsync()
        {
            await context.SaveChangesAsync();
        }

        public void LogConnectionDetails()
        {
            try
            {
                var connection = context.Database.GetDbConnection();
                var maskedConnectionString = MaskSensitiveInfo(connection.ConnectionString);
            
                Log.Information($"Current Connection Details:");
                Log.Information($"Data Source: {connection.DataSource}");
                Log.Information($"Database: {connection.Database}");
                Log.Information($"Masked Connection String: {maskedConnectionString}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error logging connection details");
            }
        }

        private string MaskSensitiveInfo(string connectionString)
        {
            // Mask password
            var maskedString = Regex.Replace(connectionString, 
                @"(Password|Pwd)=[^;]*", "$1=********", 
                RegexOptions.IgnoreCase);

            // Mask user id if present
            maskedString = Regex.Replace(maskedString, 
                @"(User ID|Uid)=[^;]*", "$1=********", 
                RegexOptions.IgnoreCase);

            return maskedString;
        }
        
        public async Task<TucClientContact> FetchUserByUsername(string email)
        {
            LogConnectionDetails();
            try
            {
                
                
                Log.Information($"Attempting to fetch user with email: {email}"); 
                
                return await context.TucClientContacts.Where(x => x.Active && x.UserName == email).Include("UcctClient").FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error fetching user with email: {email}");
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
            var data =await context.Procedures.RVW_stpValidateInternetPermissionsAsync(contactId);

            return data;
        }


        public async Task<bool> InitiatePasswordReset(int contactId, string recoveryEmail, string replyEmail, string link)
        {
            await context.Procedures.NET_stpContact_ResetPasswordAsync(contactId, recoveryEmail, replyEmail, link);
            return true;

        }

        public void UpdateUserAccessed(int id, bool rememberMe)
        {

            var contact = context.TucClientContacts.FirstOrDefault(x => x.UcctId == id);
            if (contact == null) return;
            contact.LastAccessed = DateTime.Now;
            contact.HasEmail = contact.WhenEmailValidated == null || contact.HasEmail;
            contact.ValidatedEmail = contact.WhenEmailValidated == null || contact.ValidatedEmail;
            contact.WhenEmailValidated = contact.WhenEmailValidated ?? DateTime.Now;
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
                Log.Information($"Attempting to validate courier with email: {email}");

                // Get the tucCourier record with the given email
                var courier = await context.TucCouriers
                    .FirstOrDefaultAsync(x => x.Active && x.UccrEmail != null && x.UccrEmail.Trim().ToLower() == email.ToLower().Trim());

                if (courier != null)
                {
                    Log.Information($"Found courier with ID: {courier.UccrId}");
                    return courier.UccrId;
                }

                Log.Warning($"No active courier found with email: {email}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error validating courier with email: {email}");
                return null;
            }
        }

        public async Task<bool> IsAfterHoursAuthorized(int courierId, int dayOfWeek)
        {
            LogConnectionDetails();
            try
            {
                Log.Information($"Checking after-hours authorization for courier {courierId} on day {dayOfWeek}");

                // Check if a tblAfterHoursCourier record exists for this courier and day of week
                var isAuthorized = await context.TblAfterhoursCouriers
                    .AnyAsync(ah => ah.CourierId == courierId && ah.WeekDay == dayOfWeek);

                return isAuthorized;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error checking after-hours authorization for courier {courierId} on day {dayOfWeek}");
                return false;
            }
        }
    }
}
