using Hub.Shared;
using Microsoft.Data.Sqlite;

namespace Hub.Tests.Shared;

// Branches B1: the BranchIds claim format is a contract every DFRNT app parses.
public class BranchIdsClaimTests
{
    [Fact]
    public void Type_is_exactly_BranchIds() => Assert.Equal("BranchIds", BranchIdsClaim.Type);

    [Fact]
    public void No_branches_is_an_empty_string()
    {
        Assert.Equal(string.Empty, BranchIdsClaim.Format(null));
        Assert.Equal(string.Empty, BranchIdsClaim.Format([]));
    }

    [Fact]
    public void Branches_are_ascending_comma_separated_without_spaces_or_duplicates() =>
        Assert.Equal("1,5,10,22", BranchIdsClaim.Format([22, 5, 10, 1, 5]));

    [Fact]
    public void A_missing_table_is_recognised_and_other_errors_are_not()
    {
        Assert.True(BranchIdsClaim.IsMissingTable(new SqliteException("SQLite Error 1: 'no such table: tblContactBranch'.", 1)));
        Assert.True(BranchIdsClaim.IsMissingTable(
            new InvalidOperationException("wrapped", new SqliteException("no such table: tblBranch", 1))));
        Assert.False(BranchIdsClaim.IsMissingTable(new SqliteException("database is locked", 5)));
        Assert.False(BranchIdsClaim.IsMissingTable(new TimeoutException("timeout")));
    }
}
