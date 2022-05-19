using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    }
}
