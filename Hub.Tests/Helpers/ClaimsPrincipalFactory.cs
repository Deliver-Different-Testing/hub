using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Hub.Tests.Helpers;

public static class ClaimsPrincipalFactory
{
    public static ClaimsPrincipal Create(
        string email = "staff@test.com",
        int userId = 1,
        int currentTenantId = 1,
        string contactId = "1",
        string clientId = "1",
        string staffId = "10",
        string connection = "Server=test;Database=TestDB;",
        bool rememberMe = false,
        string countryCode = "NZ",
        string timeZone = "New Zealand Standard Time",
        string tenantCode = "test",
        bool internalTenantUser = false,
        bool isCourier = false,
        int? courierId = null,
        int? accountsMode = 1)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, email),
            new("UserID", userId.ToString()),
            new("CurrentTenantID", currentTenantId.ToString()),
            new("ContactID", contactId),
            new("ClientID", clientId),
            new("StaffID", staffId),
            new("Connection", connection),
            new("CountryCode", countryCode),
            new("TimeZone", timeZone),
            new("TenantCode", tenantCode),
            new("RememberMe", rememberMe.ToString()),
            new("Internal", internalTenantUser.ToString()),
            new("IsCourier", isCourier.ToString()),
            new("CourierID", courierId?.ToString() ?? string.Empty),
            new("AccountsMode", accountsMode?.ToString() ?? "1")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public static ClaimsPrincipal CreateAnonymous() => new(new ClaimsIdentity());
}
