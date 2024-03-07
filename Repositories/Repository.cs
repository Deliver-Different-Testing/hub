using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UrgentHub.Models;

namespace UrgentHub.Repositories
{
    public class Repository
    {
        private readonly DespatchContext _context;

        public Repository(DespatchContext context)
        {
            _context = context;
        }
        
        public Client GetClient(int clientId)
        {
            var client = new Client();
            _context.LoadStoredProc("RVW_stpValidateClient")
                .WithSqlParam("@ClientID", clientId)
                .ExecuteStoredProc(handle => { client = handle.ReadToList<Client>().FirstOrDefault(); });

            return client;
        }

        public Contact GetContact(int contactId)
        {
            var contact = new Contact();
            _context.LoadStoredProc("RVW_stpValidateContact")
                .WithSqlParam("@ContactID", contactId)
                .ExecuteStoredProc(handle => { contact = handle.ReadToList<Contact>().FirstOrDefault(); });

            return contact;
        } 

        public List<InternetPermission> GetDespatchWebInternetPermissions(int contactId) 
        {
            var internetPermissions = new List<InternetPermission>();
            _context.LoadStoredProc("RVW_stpValidateInternetPermissions")
                .WithSqlParam("@ContactID", contactId)
                .ExecuteStoredProc(handle => { internetPermissions = (List<InternetPermission>)handle.ReadToList<InternetPermission>(); });

            return internetPermissions;
        }

        public List<TucClientContact> FetchUsersByUsername(string email) => _context.TucClientContacts.Where(x => x.Active && x.UserName == email).Include("TucClient").ToList();

        public void UpdateUserAccessed(int id, bool rememberMe)
        {

            var contact = _context.TucClientContacts.FirstOrDefault(x => x.UcctId == id);
            if (contact == null) return;
            contact.LastAccessed = DateTime.Now;
            contact.HasEmail = contact.WhenEmailValidated == null || contact.HasEmail;
            contact.ValidatedEmail = contact.WhenEmailValidated == null || contact.ValidatedEmail;
            contact.WhenEmailValidated = contact.WhenEmailValidated ?? DateTime.Now;
            contact.WhenEmailValidatedSent = contact.WhenEmailValidated == null
                ? DateTime.Now
                : contact.WhenEmailValidatedSent;
            contact.AllowCookieLogin = rememberMe;
            _context.SaveChangesAsync();
        }
    }
}
