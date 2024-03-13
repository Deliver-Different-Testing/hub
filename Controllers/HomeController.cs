using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Web;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UrgentHub.Repositories;
using UrgentHub.Models;
using UrgentHub.Shared;
using System.Security.Claims;

namespace UrgentHub.Controllers
{
    public class HomeController : Controller
    {
        private readonly Repository _repo;
        private readonly IConfiguration _configuration;

        public HomeController(Repository repo, IConfiguration configuration)
        {
            _repo = repo;
            _configuration = configuration;

        }

        public IActionResult Index([FromQuery] string login)
        {
            var cid = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "ContactID")?.Value;
            
            if (cid != null)
            {
                var contactDetail = _repo.GetContact(int.Parse(cid));
                var clientDetail = _repo.GetClient(contactDetail.ClientId);

                var internetPermissions = _repo.GetDespatchWebInternetPermissions(int.Parse(cid));

                ViewBag.ContactID = int.Parse(cid);
                ViewBag.ContactName = contactDetail.FirstName;
                ViewBag.ContactFullName = contactDetail.FirstName + " " + contactDetail.SurName;
                ViewBag.ContactEmail = contactDetail.UserName;
                ViewBag.ContactCreated = (Int32)(contactDetail.Created.ToUniversalTime().Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                ViewBag.GreetingString = GetGreetingString();
                ViewBag.DespatchWebPermission = GetPermission(internetPermissions, 12);
                ViewBag.BookJobPermission = GetPermission(internetPermissions, 2);

                ViewBag.ClientName = clientDetail.Name;
                ViewBag.ClientInternal = clientDetail.Internal;
                ViewBag.ClientID = contactDetail.ClientId;
                ViewBag.ClientCreated = (Int32)(clientDetail.Created.ToUniversalTime().Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                ViewBag.ClientStripe = clientDetail.StripeClient;

                return View();
            }

            
            else
            {
                return RedirectToAction("Login", "Account");
            }
        }

        public string GetGreetingString()
        {
            string[] greetings = new string[] { "Hi", "Hello", "Welcome", "Greetings", "G'day", "Hey", "Good to see you,", "How are you", "Hope it's swell,", "How's it going", "What's good", "Howdy", "Kia ora", "Tēnā koe",
            "The world is yours,", "Hi", "Hello", "Hey", "All the best,", "Enjoy,", "Seize the day,", "Kia ora" };
            Random random = new Random();
            return greetings[random.Next(greetings.Length)];
        }

        public bool GetPermission(List<InternetPermission> internetPermissions, int internetPermissionId)
        {
            foreach (InternetPermission i in internetPermissions)
            {
                if (i.InternetPermissionId == internetPermissionId)
                {
                    return true;
                }
            }

            return false;
        }

        

        public static string EncryptOutgoingToString(int userID, string page)
        {
            if (!string.IsNullOrEmpty(userID.ToString()))
            {
                var key = Convert.FromBase64String("8wzkFlOvNB8+7UgmX0bSyFPHxjLNjmaGhlUoSKkJ2Kc=");
                var iv = Convert.FromBase64String("iE1+VfQbXWBkCalh+iV85Q==");

                var id = userID.ToString();
                var data = id + "|" + DateTime.Now;

                var encData = PasswordHelper.EncryptStringToBytes_Aes(data, key, iv);

                var encryptedString = HttpUtility.UrlEncode(Convert.ToBase64String(encData));

                if (page == "bulkImport")
                    encryptedString = encryptedString.Replace("%2f", "%252f");

                return encryptedString;
            } else {
                return "";
            }
        }

        
    }
}

