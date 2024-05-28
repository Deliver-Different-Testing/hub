using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UrgentHub.Models;

namespace UrgentHub.Repositories
{
    public class Repository(DespatchContext context)
    {
        public async Task<RVW_stpValidateClientResult> GetClient(int clientId)
        {
            var data = await context.Procedures.RVW_stpValidateClientAsync(clientId);
            return data.FirstOrDefault();
        }

        public async Task<RVW_stpValidateContactResult> GetContact(int contactId)
        {

            var data = await context.Procedures.RVW_stpValidateContactAsync(contactId);


            return data.FirstOrDefault();
        } 

        public async Task<List<RVW_stpValidateInternetPermissionsResult>> GetDespatchWebInternetPermissions(int contactId)
        {
            var data =await context.Procedures.RVW_stpValidateInternetPermissionsAsync(contactId);

            return data;
        }

        public List<TucClientContact> FetchUsersByUsername(string email) => context.TucClientContacts.Where(x => x.Active && x.UserName == email).Include("UcctClient").ToList();

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
