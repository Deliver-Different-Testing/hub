using Hub.Repositories;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hub.Models;

namespace Hub.Controllers;

public class HomeController(IConnectionStringManager connectionStringManager, Repository despatchRepository)
    : Controller
{
    public async Task<IActionResult> Index()
    {
        var cid = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "ContactID")?.Value;
        var connectionString = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Connection")?.Value;
        var internalTenantUser = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Internal")?.Value;
        var userEmail = HttpContext.User.Claims.FirstOrDefault(x => x.Type == System.Security.Claims.ClaimTypes.Name)
            ?.Value;
        var tenantCode = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "TenantCode")?.Value;
        if (cid == null || connectionString == null) return RedirectToAction("Login", "Account");
        Log.Debug("Found Identity for ContactID:{Cid}", cid);
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
       
        if (string.IsNullOrEmpty(credentials)) throw new InvalidOperationException(
                "Could not find a environment variable string named 'SQLCredentials'.");

        connectionStringManager.SetConnectionString(connectionString + credentials);

        var internetPermissions = await despatchRepository.GetDespatchWebInternetPermissions(int.Parse(cid));

        ViewBag.ContactID = int.Parse(cid);
        ViewBag.GreetingString = GetGreetingString();
        ViewBag.DespatchWebPermission = GetPermission(internetPermissions, 12);
        ViewBag.BookJobPermission = GetPermission(internetPermissions, 2);
        ViewBag.BulkUploadPermission = GetPermission(internetPermissions, 11);
        ViewBag.UserEmail = userEmail;
        ViewBag.TenantCode = tenantCode;
        ViewBag.ClientInternal = internalTenantUser;
        
        // Check if courier is authorized for after-hours on current day
        ViewBag.ShowAfterHours = await IsAfterHoursAuthorizedAsync();

        return View();
    }

    private static string GetGreetingString()
    {
        var greetings = new[]
        {
            "Hi", "Hello", "Welcome", "Greetings", "G'day", "Hey", "Good to see you,", "How are you",
            "Hope it's swell,", "How's it going", "What's good", "Howdy", "Kia ora", "Tēnā koe",
            "The world is yours,", "Hi", "Hello", "Hey", "All the best,", "Enjoy,", "Seize the day,", "Kia ora"
        };
        var random = new Random();
        return greetings[random.Next(greetings.Length)];
    }

    private static bool GetPermission(List<RVW_stpValidateInternetPermissionsResult> internetPermissions,
        int internetPermissionId) => internetPermissions.Any(i => i.InternetPermissionID == internetPermissionId);

    private async Task<bool> IsAfterHoursAuthorizedAsync()
    {
        try
        {
            // Check if user is a courier
            var isCourierClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "IsCourier")?.Value;
            var courierIdClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CourierID")?.Value;

            var isCourier = !string.IsNullOrEmpty(isCourierClaim) &&
                            bool.TryParse(isCourierClaim, out var courierFlag) && courierFlag;

            // If not a courier, don't show AfterHours tile
            if (!isCourier || string.IsNullOrEmpty(courierIdClaim) || !int.TryParse(courierIdClaim, out var courierId))
                return false;

            // Get tenant timezone from claims
            var timeZoneClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "TimeZone")?.Value;
            DateTime tenantCurrentTime;

            if (!string.IsNullOrEmpty(timeZoneClaim))
            {
                try
                {
                    var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timeZoneClaim);
                    tenantCurrentTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneInfo);
                }
                catch (TimeZoneNotFoundException)
                {
                    Log.Warning("TimeZone '{TimeZoneClaim}' not found, falling back to server time", timeZoneClaim);
                    tenantCurrentTime = DateTime.Now;
                }
            }
            else
            {
                Log.Warning("TimeZone claim not found, falling back to server time");
                tenantCurrentTime = DateTime.Now;
            }

            // Get current day of week in tenant timezone (0 = Sunday, 6 = Saturday)
            var currentDayOfWeek = (int)tenantCurrentTime.DayOfWeek;

            // Check if courier is scheduled for after-hours on current day
            var isAuthorized = await despatchRepository.IsAfterHoursAuthorized(courierId, currentDayOfWeek);

            Log.Information(
                "AfterHours authorization check for courier {CourierId} on {DayOfWeek} (tenant time: {TenantCurrentTime:yyyy-MM-dd HH:mm:ss}, timezone: {TimeZoneClaim}): {IsAuthorized}",
                courierId, tenantCurrentTime.DayOfWeek, tenantCurrentTime, timeZoneClaim ?? "N/A", isAuthorized);

            return isAuthorized;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking after-hours authorization");
            return false;
        }
    }
}