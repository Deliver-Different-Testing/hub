using System.Security.Claims;
using Hub.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Hub.Tests.Services;

// A cookie lives for hours, so the BranchIds claim written at sign-in goes stale when an admin changes someone's branches.
// The refresher re-reads the scope now and then and hands back a rebuilt principal, and it must never widen on a failure.
public class BranchClaimRefresherTests
{
    private const string Conn = "Server=tenant;Database=d;";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClaimsPrincipal Principal(
        string? branchIds, int clientType = 4, bool networkPartner = false, bool courier = false, bool authenticated = true)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "sam@test.com"),
            new("UserID", "55"),
            new("CurrentTenantID", "12"),
            new("ContactID", "9"),
            new("Connection", Conn),
            new("ClientTypeId", clientType.ToString()),
            new("IsNetworkPartner", networkPartner.ToString()),
            new("IsCourier", courier.ToString())
        };
        if (branchIds is not null) claims.Add(new Claim("BranchIds", branchIds));
        return new ClaimsPrincipal(authenticated ? new ClaimsIdentity(claims, "Identity.Application") : new ClaimsIdentity(claims));
    }

    private static (BranchClaimRefresher Refresher, IContactBranchReader Reader) Build(
        IReadOnlyList<int>? scope, TimeSpan? interval = null)
    {
        var reader = Substitute.For<IContactBranchReader>();
        reader.ReadAsync(Conn, 9, Arg.Any<CancellationToken>()).Returns(scope);
        return (Refresher(reader, interval), reader);
    }

    private static BranchClaimRefresher Refresher(IContactBranchReader reader, TimeSpan? interval = null) =>
        new(reader, new MemoryCache(new MemoryCacheOptions()), new BranchClaimRefreshOptions(interval ?? TimeSpan.FromMinutes(5)));

    [Fact]
    public async Task BranchesAddedAfterSignIn_ChangeThePrincipal_AndKeepEveryOtherClaim()
    {
        var (refresher, _) = Build([2, 1]);

        var rebuilt = await refresher.RefreshAsync(Principal(branchIds: ""), Ct);

        Assert.NotNull(rebuilt);
        Assert.Equal("1,2", rebuilt.FindFirst("BranchIds")!.Value);
        Assert.Single(rebuilt.FindAll("BranchIds"));
        Assert.Equal("sam@test.com", rebuilt.Identity!.Name);
        Assert.Equal("55", rebuilt.FindFirst("UserID")!.Value);
        Assert.Equal(Conn, rebuilt.FindFirst("Connection")!.Value);
        Assert.Equal("Identity.Application", rebuilt.Identity.AuthenticationType);
        Assert.True(rebuilt.Identity.IsAuthenticated);
    }

    [Fact]
    public async Task BranchesRemovedAfterSignIn_ChangeThePrincipal_ToNoScope()
    {
        var (refresher, _) = Build([]);

        var rebuilt = await refresher.RefreshAsync(Principal(branchIds: "1,2"), Ct);

        Assert.NotNull(rebuilt);
        Assert.Equal(string.Empty, rebuilt.FindFirst("BranchIds")!.Value);
    }

    [Fact]
    public async Task ABranchChangedFromOneToAnother_IsPickedUp()
    {
        var (refresher, _) = Build([3]);

        var rebuilt = await refresher.RefreshAsync(Principal(branchIds: "1"), Ct);

        Assert.Equal("3", rebuilt!.FindFirst("BranchIds")!.Value);
    }

    [Fact]
    public async Task ACookieIssuedBeforeTheClaimExisted_GetsTheClaim_SoALimitedPersonIsNoLongerTreatedAsUnrestricted()
    {
        var (refresher, _) = Build([4]);

        var rebuilt = await refresher.RefreshAsync(Principal(branchIds: null), Ct);

        Assert.Equal("4", rebuilt!.FindFirst("BranchIds")!.Value);
    }

    [Fact]
    public async Task ACookieWithNoClaimForSomeoneWithNoScope_IsGivenAnEmptyClaimOnce()
    {
        var (refresher, _) = Build([]);

        var rebuilt = await refresher.RefreshAsync(Principal(branchIds: null), Ct);

        Assert.Equal(string.Empty, rebuilt!.FindFirst("BranchIds")!.Value);
    }

    [Fact]
    public async Task WhenNothingChanged_ThePrincipalIsLeftAlone()
    {
        var (refresher, _) = Build([1, 2]);

        Assert.Null(await refresher.RefreshAsync(Principal(branchIds: "1,2"), Ct));
    }

    [Fact]
    public async Task ADatabaseFailure_KeepsThePrincipal_SoALimitedPersonIsNeverWidened()
    {
        var reader = Substitute.For<IContactBranchReader>();
        reader.ReadAsync(Conn, 9, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database down"));

        var rebuilt = await Refresher(reader).RefreshAsync(Principal(branchIds: "1,2"), Ct);

        Assert.Null(rebuilt);
    }

    [Fact]
    public async Task AContactThatIsGoneOrInactive_KeepsThePrincipal()
    {
        var (refresher, _) = Build(null);

        Assert.Null(await refresher.RefreshAsync(Principal(branchIds: "1,2"), Ct));
    }

    [Fact]
    public async Task AFailedReadIsNotRetriedOnEveryRequest()
    {
        var reader = Substitute.For<IContactBranchReader>();
        reader.ReadAsync(Conn, 9, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database down"));
        var refresher = Refresher(reader);

        await refresher.RefreshAsync(Principal(branchIds: "1"), Ct);
        await refresher.RefreshAsync(Principal(branchIds: "1"), Ct);

        await reader.Received(1).ReadAsync(Conn, 9, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheDatabaseIsReadOncePerInterval_NotOnEveryRequest()
    {
        var (refresher, reader) = Build([1, 2]);

        await refresher.RefreshAsync(Principal(branchIds: "1,2"), Ct);
        await refresher.RefreshAsync(Principal(branchIds: "1,2"), Ct);
        await refresher.RefreshAsync(Principal(branchIds: "1,2"), Ct);

        await reader.Received(1).ReadAsync(Conn, 9, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AZeroInterval_ReadsOnEveryRequest()
    {
        var (refresher, reader) = Build([1, 2], TimeSpan.Zero);

        await refresher.RefreshAsync(Principal(branchIds: "1,2"), Ct);
        await refresher.RefreshAsync(Principal(branchIds: "1,2"), Ct);

        await reader.Received(2).ReadAsync(Conn, 9, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AChangeIsSeenOnTheNextReadOnceTheIntervalIsUp()
    {
        var reader = Substitute.For<IContactBranchReader>();
        reader.ReadAsync(Conn, 9, Arg.Any<CancellationToken>()).Returns([1], [1, 2]);
        var refresher = Refresher(reader, TimeSpan.Zero);

        Assert.Null(await refresher.RefreshAsync(Principal(branchIds: "1"), Ct));
        var rebuilt = await refresher.RefreshAsync(Principal(branchIds: "1"), Ct);

        Assert.Equal("1,2", rebuilt!.FindFirst("BranchIds")!.Value);
    }

    [Theory]
    [InlineData(3, false, false)]   // network partner client
    [InlineData(2, false, false)]   // customer
    [InlineData(5, false, false)]   // DF Admin
    [InlineData(4, false, true)]    // courier
    public async Task NonStaff_AreNeverTouched_AndTheDatabaseIsNotRead(int clientType, bool networkPartner, bool courier)
    {
        var (refresher, reader) = Build([1, 2]);

        var rebuilt = await refresher.RefreshAsync(Principal(branchIds: "", clientType, networkPartner, courier), Ct);

        Assert.Null(rebuilt);
        await reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default, default);
    }

    [Fact]
    public async Task TheMasterLevelNetworkPartnerFlag_DoesNotStopAStaffContactBeingRefreshed()
    {
        // IsNetworkPartner is one flag across every tenant. This person is a partner somewhere else but tenant STAFF
        // (client type 4) here, with branch rows here: the branch limit must still follow them.
        var (refresher, _) = Build([1, 2]);

        var rebuilt = await refresher.RefreshAsync(Principal(branchIds: "", clientType: 4, networkPartner: true), Ct);

        Assert.Equal("1,2", rebuilt!.FindFirst("BranchIds")!.Value);
    }

    // Sign-in reads the scope live, so it seeds the cache; otherwise an older cached read overwrites the fresh cookie.
    [Fact]
    public async Task AFreshSignIn_IsNotUndoneByAnOlderCachedRead_TheUnrestrictedThenLimitedCase()
    {
        var reader = Substitute.For<IContactBranchReader>();
        reader.ReadAsync(Conn, 9, Arg.Any<CancellationToken>()).Returns(Array.Empty<int>());
        var refresher = Refresher(reader);

        // The person is unrestricted and a refresh caches that.
        Assert.Null(await refresher.RefreshAsync(Principal(branchIds: ""), Ct));

        // An admin limits them to branch 1. They sign out and in again: sign-in read "1" and seeds it.
        refresher.Seed(12, 9, "1");
        var afterSignIn = await refresher.RefreshAsync(Principal(branchIds: "1"), Ct);

        Assert.Null(afterSignIn);   // not re-issued as unrestricted
        await reader.Received(1).ReadAsync(Conn, 9, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutSeeding_AnOlderCachedReadWouldHaveWidenedAccess_ProvingTheSeedMatters()
    {
        var reader = Substitute.For<IContactBranchReader>();
        reader.ReadAsync(Conn, 9, Arg.Any<CancellationToken>()).Returns(Array.Empty<int>());
        var refresher = Refresher(reader);
        await refresher.RefreshAsync(Principal(branchIds: ""), Ct);

        var rebuilt = await refresher.RefreshAsync(Principal(branchIds: "1"), Ct);   // sign-in did not seed

        Assert.Equal(string.Empty, rebuilt!.FindFirst("BranchIds")!.Value);          // the old read wins: unrestricted
    }

    [Fact]
    public async Task ALimitRemovedThenSignedInAgain_IsNotNarrowedByTheOlderCachedRead()
    {
        var reader = Substitute.For<IContactBranchReader>();
        reader.ReadAsync(Conn, 9, Arg.Any<CancellationToken>()).Returns(new[] { 1 });
        var refresher = Refresher(reader);
        Assert.Null(await refresher.RefreshAsync(Principal(branchIds: "1"), Ct));   // caches "1"

        refresher.Seed(12, 9, "");   // limit removed, re-signed in: sign-in read an empty scope

        Assert.Null(await refresher.RefreshAsync(Principal(branchIds: ""), Ct));
    }

    [Fact]
    public async Task ATenantSwitch_AToBToA_KeepsEachTenantsScopeAndNeverReadsAgain()
    {
        var reader = Substitute.For<IContactBranchReader>();
        reader.ReadAsync(Arg.Any<string>(), 9, Arg.Any<CancellationToken>()).Returns(Array.Empty<int>());
        var refresher = Refresher(reader);
        var tenantA = Principal(branchIds: "");                       // tenant 12
        Assert.Null(await refresher.RefreshAsync(tenantA, Ct));       // A cached as "" (one read)

        refresher.Seed(13, 9, "2");   // switch to B: scoped to branch 2
        refresher.Seed(12, 9, "1");   // switch back to A: scoped to branch 1 since (admin changed it meanwhile)

        var tenantB = new ClaimsPrincipal(new ClaimsIdentity(
            Principal(branchIds: "2").Claims.Where(c => c.Type != "CurrentTenantID").Append(new Claim("CurrentTenantID", "13")),
            "Identity.Application"));
        Assert.Null(await refresher.RefreshAsync(tenantB, Ct));
        Assert.Null(await refresher.RefreshAsync(Principal(branchIds: "1"), Ct));
        await reader.Received(1).ReadAsync(Arg.Any<string>(), 9, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedingDoesNothingWhenTheIntervalIsZero_SoEveryRequestStillReadsTheDatabase()
    {
        var (refresher, reader) = Build([1], TimeSpan.Zero);
        refresher.Seed(12, 9, "9");

        var rebuilt = await refresher.RefreshAsync(Principal(branchIds: "9"), Ct);

        Assert.Equal("1", rebuilt!.FindFirst("BranchIds")!.Value);
        await reader.Received(1).ReadAsync(Conn, 9, Arg.Any<CancellationToken>());
    }

    // The cookie hook: a real validate-principal context, so what happens to the ticket's properties is proven, not assumed.
    private static CookieValidatePrincipalContext CookieContext(
        BranchClaimRefresher refresher, ClaimsPrincipal principal, AuthenticationProperties properties)
    {
        var services = new ServiceCollection().AddSingleton(refresher).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme("Identity.Application", null, typeof(CookieAuthenticationHandler));
        return new CookieValidatePrincipalContext(http, scheme, new CookieAuthenticationOptions(),
            new AuthenticationTicket(principal, properties, "Identity.Application"));
    }

    [Fact]
    public async Task TheCookieHook_ReplacesThePrincipalAndRenews_KeepingRememberMePersistenceAndExpiry()
    {
        var (refresher, _) = Build([1, 2]);
        // AuthenticationProperties keeps whole seconds, so use whole seconds to compare like with like.
        var expires = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds());
        var issued = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeSeconds());
        var properties = new AuthenticationProperties { IsPersistent = true, ExpiresUtc = expires, IssuedUtc = issued, AllowRefresh = true };
        var context = CookieContext(refresher, Principal(branchIds: ""), properties);

        await BranchClaimCookie.ValidateAsync(context);

        Assert.Equal("1,2", context.Principal!.FindFirst("BranchIds")!.Value);
        Assert.True(context.ShouldRenew);
        Assert.True(context.Properties.IsPersistent);
        Assert.Equal(expires, context.Properties.ExpiresUtc);
        Assert.Equal(issued, context.Properties.IssuedUtc);
        Assert.Equal("sam@test.com", context.Principal.Identity!.Name);
    }

    [Fact]
    public async Task TheCookieHook_LeavesAnUnchangedPrincipalAlone_NoRenewal()
    {
        var (refresher, _) = Build([1, 2]);
        var context = CookieContext(refresher, Principal(branchIds: "1,2"), new AuthenticationProperties());
        var original = context.Principal;

        await BranchClaimCookie.ValidateAsync(context);

        Assert.Same(original, context.Principal);
        Assert.False(context.ShouldRenew);
    }

    [Fact]
    public async Task TransitionalInternalStaff_AreRefreshedLikeStaff()
    {
        var (refresher, _) = Build([7]);

        var rebuilt = await refresher.RefreshAsync(Principal(branchIds: "", clientType: 1), Ct);

        Assert.Equal("7", rebuilt!.FindFirst("BranchIds")!.Value);
    }

    [Fact]
    public async Task AnUnauthenticatedOrMissingPrincipal_IsLeftAlone()
    {
        var (refresher, reader) = Build([1]);

        Assert.Null(await refresher.RefreshAsync(null, Ct));
        Assert.Null(await refresher.RefreshAsync(Principal(branchIds: "", authenticated: false), Ct));
        await reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default, default);
    }

    [Fact]
    public async Task APrincipalMissingTheClaimsNeededToLookItUp_IsLeftAlone()
    {
        var (refresher, reader) = Build([1]);
        var noContact = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("ClientTypeId", "4"), new Claim("IsNetworkPartner", "False"), new Claim("Connection", Conn),
             new Claim("CurrentTenantID", "12")], "Identity.Application"));

        Assert.Null(await refresher.RefreshAsync(noContact, Ct));
        await reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default, default);
    }

    [Fact]
    public async Task OnlyOneScopeIsCachedPerContactAndTenant()
    {
        var reader = Substitute.For<IContactBranchReader>();
        reader.ReadAsync(Conn, 9, Arg.Any<CancellationToken>()).Returns([1]);
        reader.ReadAsync(Conn, 10, Arg.Any<CancellationToken>()).Returns([2]);
        var refresher = Refresher(reader);

        Assert.Null(await refresher.RefreshAsync(Principal(branchIds: "1"), Ct));
        var other = Principal(branchIds: "");
        var otherContact = new ClaimsPrincipal(new ClaimsIdentity(
            other.Claims.Where(c => c.Type != "ContactID").Append(new Claim("ContactID", "10")), "Identity.Application"));

        var rebuilt = await refresher.RefreshAsync(otherContact, Ct);

        Assert.Equal("2", rebuilt!.FindFirst("BranchIds")!.Value);
    }
}
