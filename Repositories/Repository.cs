using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.Marshalling;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using UrgentHub.Models;

namespace UrgentHub.Repositories
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


        public async Task<List<RVW_stpValidateInternetPermissionsResult>> GetDespatchWebInternetPermissions(int contactId)
        {
            var data =await context.Procedures.RVW_stpValidateInternetPermissionsAsync(contactId);

            return data;
        }


        public async Task<bool> InitiatePasswordReset(string recoveryEmail, string replyEmail, string link)
        {
            await context.Procedures.NET_stpContact_ResetPasswordAsync(recoveryEmail, replyEmail, link);
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
    }
}
