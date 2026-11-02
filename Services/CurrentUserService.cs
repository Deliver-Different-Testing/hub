using System.Security.Claims;
using Hub.Interfaces;
using Hub.ViewModels;

namespace Hub.Services;

public class CurrentUserService(ITenantService tenantService) : ICurrentUserService
{
    public async Task<MeResponse> GetMeAsync(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst("UserID");
        var currentTenantIdClaim = user.FindFirst("CurrentTenantID");

        List<MeTenant> tenants = [];
        int? currentTenantId = null;
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
        {
            var tenantList = await tenantService.GetTenantsForUserAsync(userId);
            tenants = tenantList.Select(o => new MeTenant(o.TenantId, o.Name, o.Code, o.CountryCode)).ToList();

            if (currentTenantIdClaim != null && int.TryParse(currentTenantIdClaim.Value, out var orgId))
            {
                currentTenantId = orgId;
            }
        }

        var currentTenantCode = user.FindFirst("TenantCode")?.Value ?? string.Empty;
        var currentTenantCountry = user.FindFirst("CountryCode")?.Value;

        MeTenant? currentTenant = null;
        if (currentTenantId is { } id)
        {
            var match = tenants.FirstOrDefault(t => t.Id == id);
            currentTenant = new MeTenant(
                id,
                match?.Name ?? string.Empty,
                currentTenantCode,
                string.IsNullOrEmpty(currentTenantCountry) ? match?.CountryCode : currentTenantCountry);
        }

        var userEmail = user.Identity?.Name ?? string.Empty;
        var firstName = user.FindFirst("FirstName")?.Value.Trim() ?? string.Empty;
        var surname = user.FindFirst("Surname")?.Value.Trim() ?? string.Empty;
        var displayName = string.Join(' ', new[] { firstName, surname }.Where(p => !string.IsNullOrEmpty(p)));
        if (string.IsNullOrEmpty(displayName))
        {
            var nameParts = userEmail
                .Split('@')[0]
                .Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Length > 0 ? char.ToUpper(p[0]) + p[1..].ToLower() : p)
                .ToArray();
            displayName = string.Join(' ', nameParts);
        }

        var isCourier = string.Equals(user.FindFirst("IsCourier")?.Value, "True", StringComparison.OrdinalIgnoreCase);

        return new MeResponse(
            userEmail,
            string.IsNullOrEmpty(displayName) ? null : displayName,
            isCourier,
            currentTenant,
            tenants);
    }
}
