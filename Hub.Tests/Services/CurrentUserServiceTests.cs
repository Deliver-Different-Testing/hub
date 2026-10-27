using System.Security.Claims;
using Hub.Interfaces;
using Hub.Models.Master;
using Hub.Services;
using Hub.Tests.Helpers;
using NSubstitute;

namespace Hub.Tests.Services;

public class CurrentUserServiceTests
{
    private static CurrentUserService Create(params Tenant[] tenants)
    {
        var tenantService = Substitute.For<ITenantService>();
        tenantService.GetTenantsForUserAsync(Arg.Any<int>()).Returns(tenants);
        return new CurrentUserService(tenantService);
    }

    private static Tenant T(int id, string name, string code, string? country) =>
        new() { TenantId = id, Name = name, Code = code, CountryCode = country!, Dbconnection = "x", TimeZone = "UTC" };

    private static ClaimsPrincipal WithClaims(params (string Type, string Value)[] extra)
    {
        var claims = ClaimsPrincipalFactory.Create().Claims
            .Where(c => extra.All(e => e.Type != c.Type))
            .Concat(extra.Select(e => new Claim(e.Type, e.Value)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task GetMe_ListsTheUsersTenantsAndMarksTheCurrentOne()
    {
        var service = Create(T(1, "Urgent", "urgent", "NZ"), T(2, "Acme", "acme", "US"));
        var user = ClaimsPrincipalFactory.Create(currentTenantId: 2, tenantCode: "acme", countryCode: "US");

        var me = await service.GetMeAsync(user);

        Assert.Equal(["Urgent", "Acme"], me.Tenants.Select(t => t.Name).ToArray());
        Assert.Equal(2, me.CurrentTenant!.Id);
        Assert.Equal("Acme", me.CurrentTenant.Name);
        Assert.Equal("acme", me.CurrentTenant.Code);
        Assert.Equal("US", me.CurrentTenant.CountryCode);
    }

    [Fact]
    public async Task GetMe_TenantCountryMayBeNull()
    {
        var service = Create(T(1, "Urgent", "urgent", null));

        var me = await service.GetMeAsync(WithClaims(("CountryCode", "")));

        Assert.Null(me.Tenants[0].CountryCode);
        Assert.Null(me.CurrentTenant!.CountryCode);
    }

    [Fact]
    public async Task GetMe_AnonymousOfTenants_ReturnsEmptyTenantList()
    {
        var service = Create();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "a@b.c")], "test"));

        var me = await service.GetMeAsync(user);

        Assert.Empty(me.Tenants);
        Assert.Null(me.CurrentTenant);
        Assert.Equal("a@b.c", me.Email);
    }

    [Fact]
    public async Task GetMe_DisplayName_UsesFirstAndSurname()
    {
        var service = Create();

        var me = await service.GetMeAsync(WithClaims(("FirstName", " Jane "), ("Surname", "Doe ")));

        Assert.Equal("Jane Doe", me.DisplayName);
    }

    [Fact]
    public async Task GetMe_DisplayName_FallsBackToTheEmailLocalPart()
    {
        var service = Create();

        var me = await service.GetMeAsync(WithClaims(
            (ClaimTypes.Name, "jane.van-dyke_smith@test.com"), ("FirstName", ""), ("Surname", "")));

        Assert.Equal("Jane Van Dyke Smith", me.DisplayName);
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData("", false)]
    public async Task GetMe_IsCourier_ReadsTheClaim(string claim, bool expected)
    {
        var service = Create();

        var me = await service.GetMeAsync(WithClaims(("IsCourier", claim)));

        Assert.Equal(expected, me.IsCourier);
    }

    [Fact]
    public async Task GetMe_PassesTheUserIdClaimToTheTenantService()
    {
        var tenantService = Substitute.For<ITenantService>();
        tenantService.GetTenantsForUserAsync(Arg.Any<int>()).Returns([]);
        var service = new CurrentUserService(tenantService);

        await service.GetMeAsync(ClaimsPrincipalFactory.Create(userId: 9));

        await tenantService.Received(1).GetTenantsForUserAsync(9);
    }
}
