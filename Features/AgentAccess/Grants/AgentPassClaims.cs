using System.Security.Claims;

namespace Hub.Features.AgentAccess.Grants;

/// <summary>Everything the app pass says about the person, read fresh from the tenant when the pass is issued.</summary>
public sealed record AgentPassIdentity(
    string Email,
    int UserId,
    int TenantId,
    int ContactId,
    int ClientId,
    int StaffId,
    string CountryCode,
    string TimeZone,
    string TenantCode,
    bool Internal,
    bool IsNetworkPartner,
    int? ContactRoleId,
    int? ClientTypeId,
    int? NpAgentId,
    string FirstName,
    string Surname,
    int? AccountsMode);

// The app pass claims (phase 1.9): exactly the cookie's claim types, so apps that read User.FindFirst("StaffID")
// keep working. Never Connection, RememberMe or CourierID. The cookie list lives in AccountController.GenerateClaims.
public static class AgentPassClaims
{
    public static List<Claim> Build(AgentPassIdentity i)
    {
        var role = i.ContactRoleId?.ToString() ?? string.Empty;
        return
        [
            new(ClaimTypes.Name, i.Email ?? string.Empty),
            new("UserID", i.UserId.ToString()),
            new("CurrentTenantID", i.TenantId.ToString()),
            new("ContactID", i.ContactId.ToString()),
            new("ClientID", i.ClientId.ToString()),
            new("StaffID", i.StaffId.ToString()),
            new("CountryCode", i.CountryCode ?? string.Empty),
            new("TimeZone", i.TimeZone ?? string.Empty),
            new("TenantCode", i.TenantCode ?? string.Empty),
            new("Internal", i.Internal.ToString()),
            new("IsCourier", false.ToString()),
            new("IsNetworkPartner", i.IsNetworkPartner.ToString()),
            new("AccountsMode", i.AccountsMode?.ToString() ?? "1"),
            new("FirstName", i.FirstName ?? string.Empty),
            new("Surname", i.Surname ?? string.Empty),
            new("NpRoleId", role),
            new("RoleId", role),
            new("ClientTypeId", i.ClientTypeId?.ToString() ?? string.Empty),
            new("NpAgentId", i.NpAgentId?.ToString() ?? string.Empty)
        ];
    }
}
