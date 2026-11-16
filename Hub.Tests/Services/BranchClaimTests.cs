using Hub.Services;

namespace Hub.Tests.Services;

// The BranchIds claim: ascending, de-duplicated, comma separated; empty means no scope (sees every branch).
public class BranchClaimTests
{
    [Fact]
    public void Format_SortsAndJoins() => Assert.Equal("1,2,10", BranchClaim.Format([10, 2, 1]));

    [Fact]
    public void Format_DropsDuplicates() => Assert.Equal("3", BranchClaim.Format([3, 3, 3]));

    [Fact]
    public void Format_OfNothing_IsEmpty_MeaningNoScope()
    {
        Assert.Equal(string.Empty, BranchClaim.Format([]));
        Assert.Equal(string.Empty, BranchClaim.Format(null));
    }

    [Fact]
    public void Format_DropsNonPositiveIds_WhenAValidBranchRemains_SoOneStrayRowDoesNotLockThePersonOut()
    {
        Assert.Equal("2,5", BranchClaim.Format([5, 0, 2, -1]));
        Assert.Equal("1", BranchClaim.Format([0, 1]));
    }

    [Fact]
    public void Format_KeepsTheClaimMalformed_WhenNoValidBranchRemains_SoItStillFailsClosed()
    {
        // Dropping them all would give an empty claim, which means "every branch": a data error must never widen access.
        Assert.Equal("0", BranchClaim.Format([0]));
        Assert.Equal("-1,0", BranchClaim.Format([0, -1]));
    }

    // Decided by the tenant contact's client type alone: the master-level IsNetworkPartner flag is not an input.
    [Theory]
    [InlineData(4, false, true)]    // tenant staff
    [InlineData(1, false, true)]    // transitional Internal staff
    [InlineData(3, false, false)]   // network partner client
    [InlineData(2, false, false)]   // customer
    [InlineData(5, false, false)]   // DF Admin
    [InlineData(6, false, false)]   // connected tenant
    [InlineData(4, true, false)]    // courier
    public void IsLimitable_OnlyForTenantStaff(int clientType, bool courier, bool expected) =>
        Assert.Equal(expected, BranchClaim.IsLimitable(clientType, courier));

    [Fact]
    public void IsLimitable_WithNoClientType_IsFalse() => Assert.False(BranchClaim.IsLimitable(null));

    [Fact]
    public void For_GivesStaffTheirBranches_AndEveryoneElseAnEmptyClaim()
    {
        Assert.Equal("1,2", BranchClaim.For(4, [2, 1]));
        Assert.Equal(string.Empty, BranchClaim.For(3, [2, 1]));
        Assert.Equal(string.Empty, BranchClaim.For(5, [2, 1]));
        Assert.Equal(string.Empty, BranchClaim.For(4, [2, 1], isCourier: true));
    }

    [Fact]
    public void TheClaimTypeIsBranchIds() => Assert.Equal("BranchIds", BranchClaim.Type);
}
