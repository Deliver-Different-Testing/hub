using Hub.Repositories;
using Hub.Services;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hub.Models;

namespace Hub.Controllers
{
    public class HomeController(IConnectionStringManager connectionStringManager, Repository despatchRepository, ITenantService tenantService) : Controller
    {

        public async Task<IActionResult> Index()
        {
            var cid = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "ContactID")?.Value;
            var connectionString = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Connection")?.Value;
            var internalTenantUser = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Internal")?.Value;
            var userEmail = HttpContext.User.Claims.FirstOrDefault(x => x.Type == System.Security.Claims.ClaimTypes.Name)?.Value;
            var tenantCode = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "TenantCode")?.Value;
            if (cid != null && connectionString!= null)
            {
                Log.Debug($"Found Identity for ContactID:{cid}");
                var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? "";
                if (string.IsNullOrEmpty(credentials))
                {
                    throw new InvalidOperationException(
                        "Could not find a environment variable string named 'SQLCredentials'.");
                }
                connectionStringManager.SetConnectionString(connectionString+credentials);
                //var contactDetail = await despatchRepository.GetContact(int.Parse(cid));
                //var clientDetail = await despatchRepository.GetClient((int)contactDetail.ClientID);

                var internetPermissions = await despatchRepository.GetDespatchWebInternetPermissions(int.Parse(cid));

                ViewBag.ContactID = int.Parse(cid);
                //ViewBag.ContactName = contactDetail.FirstName;
                //ViewBag.ContactFullName = contactDetail.FirstName + " " + contactDetail.SurName;
                //ViewBag.ContactEmail = contactDetail.UserName;
                //ViewBag.ContactCreated = (int)(contactDetail.Created.ToUniversalTime().Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                ViewBag.GreetingString = GetGreetingString();
                ViewBag.DespatchWebPermission = GetPermission(internetPermissions, 12);
                ViewBag.BookJobPermission = GetPermission(internetPermissions, 2);
                ViewBag.BulkUploadPermission = GetPermission(internetPermissions, 11);
                ViewBag.UserEmail = userEmail;
                ViewBag.TenantCode = tenantCode;

                //ViewBag.ClientName = clientDetail.Name;
                ViewBag.ClientInternal = internalTenantUser;
                //ViewBag.ClientID = contactDetail.ClientID;
                //ViewBag.ClientCreated = (Int32)(clientDetail.Created.ToUniversalTime().Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                //ViewBag.ClientStripe = clientDetail.StripeClient;

                // Check if courier is authorized for after-hours on current day
                ViewBag.ShowAfterHours = await IsAfterHoursAuthorizedAsync();

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

        public bool GetPermission(List<RVW_stpValidateInternetPermissionsResult> internetPermissions, int internetPermissionId)
        {
            foreach (var i in internetPermissions)
            {
                if (i.InternetPermissionID == internetPermissionId)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<bool> IsAfterHoursAuthorizedAsync()
        {
            try
            {
                // Check if user is a courier
                var isCourierClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "IsCourier")?.Value;
                var courierIdClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CourierID")?.Value;

                bool isCourier = !string.IsNullOrEmpty(isCourierClaim) && bool.TryParse(isCourierClaim, out var courierFlag) && courierFlag;

                // If not a courier, don't show AfterHours tile
                if (!isCourier || string.IsNullOrEmpty(courierIdClaim) || !int.TryParse(courierIdClaim, out var courierId))
                {
                    return false;
                }

                // Get current day of week (0 = Sunday, 6 = Saturday)
                int currentDayOfWeek = (int)DateTime.Now.DayOfWeek;

                // Check if courier is scheduled for after-hours on current day
                var isAuthorized = await despatchRepository.IsAfterHoursAuthorized(courierId, currentDayOfWeek);

                Log.Information($"AfterHours authorization check for courier {courierId} on {DateTime.Now.DayOfWeek}: {isAuthorized}");

                return isAuthorized;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking after-hours authorization");
                return false;
            }
        }


    }
}

