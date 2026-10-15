using System.Security.Claims;
using Hub.Features.AgentAccess;

namespace Hub.Tests.Features.AgentAccess;

// Phase 1.9: the app pass carries exactly the cookie's claim types, so apps that read User.FindFirst("StaffID")
// keep working, and never the secrets a cookie holds.
public class AgentPassClaimsTests
{
    private static AgentPassIdentity Identity(
        int? clientType = 4, int? roleId = 2, int? npAgent = null, int? accountsMode = 1, bool internalUser = true, bool np = false) =>
        new(Email: "staff@urgent.test", UserId: 55, TenantId: 12, ContactId: 9, ClientId: 3, StaffId: 7,
            CountryCode: "NZ", TimeZone: "New Zealand Standard Time", TenantCode: "urgent", Internal: internalUser,
            IsNetworkPartner: np, ContactRoleId: roleId, ClientTypeId: clientType, NpAgentId: npAgent,
            FirstName: "Sam", Surname: "Staff", AccountsMode: accountsMode);

    private static Dictionary<string, string> Build(AgentPassIdentity? identity = null) =>
        AgentPassClaims.Build(identity ?? Identity()).ToDictionary(c => c.Type, c => c.Value);

    [Fact]
    public void Carries_exactly_the_cookie_claim_types_the_apps_read()
    {
        var claims = Build();

        Assert.Equal(
            new[]
            {
                ClaimTypes.Name, "UserID", "CurrentTenantID", "StaffID", "ContactID", "ClientID", "ClientTypeId", "TenantCode",
                "CountryCode", "TimeZone", "Internal", "IsCourier", "IsNetworkPartner", "NpRoleId", "RoleId", "NpAgentId",
                "FirstName", "Surname", "AccountsMode"
            }.OrderBy(x => x),
            claims.Keys.OrderBy(x => x));
    }

    [Fact]
    public void Never_carries_a_connection_string_remember_me_or_a_courier_id()
    {
        var claims = Build();

        Assert.DoesNotContain("Connection", claims.Keys);
        Assert.DoesNotContain("RememberMe", claims.Keys);
        Assert.DoesNotContain("CourierID", claims.Keys);
    }

    [Fact]
    public void Values_match_what_the_cookie_would_hold()
    {
        var claims = Build();

        Assert.Equal("staff@urgent.test", claims[ClaimTypes.Name]);
        Assert.Equal("55", claims["UserID"]);
        Assert.Equal("12", claims["CurrentTenantID"]);
        Assert.Equal("7", claims["StaffID"]);
        Assert.Equal("9", claims["ContactID"]);
        Assert.Equal("3", claims["ClientID"]);
        Assert.Equal("4", claims["ClientTypeId"]);
        Assert.Equal("urgent", claims["TenantCode"]);
        Assert.Equal("NZ", claims["CountryCode"]);
        Assert.Equal("New Zealand Standard Time", claims["TimeZone"]);
        Assert.Equal("True", claims["Internal"]);
        Assert.Equal("False", claims["IsCourier"]);
        Assert.Equal("False", claims["IsNetworkPartner"]);
        Assert.Equal("Sam", claims["FirstName"]);
        Assert.Equal("Staff", claims["Surname"]);
        Assert.Equal("1", claims["AccountsMode"]);
    }

    [Fact]
    public void The_role_is_written_to_both_claims_during_the_transition_and_empty_when_absent()
    {
        var withRole = Build(Identity(roleId: 2));
        Assert.Equal("2", withRole["RoleId"]);
        Assert.Equal("2", withRole["NpRoleId"]);

        var without = Build(Identity(roleId: null));
        Assert.Equal(string.Empty, without["RoleId"]);
        Assert.Equal(string.Empty, without["NpRoleId"]);
    }

    [Fact]
    public void Optional_values_default_as_the_cookie_does()
    {
        var claims = Build(Identity(accountsMode: null, npAgent: null));

        Assert.Equal("1", claims["AccountsMode"]);
        Assert.Equal(string.Empty, claims["NpAgentId"]);
        Assert.Equal("777", Build(Identity(npAgent: 777))["NpAgentId"]);
    }

    [Fact]
    public void A_staff_pass_is_never_a_courier()
    {
        Assert.Equal("False", Build()["IsCourier"]);
    }

    [Fact]
    public void Claim_values_are_never_null()
    {
        var identity = Identity() with { FirstName = null!, Surname = null!, Email = null!, TenantCode = null! };

        Assert.All(AgentPassClaims.Build(identity), claim => Assert.NotNull(claim.Value));
    }
}
