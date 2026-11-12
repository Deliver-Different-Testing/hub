using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Hub.Shared;

/// <summary>
/// The <c>BranchIds</c> claim (Branches B1). Every DFRNT app parses it, so the format is a contract:
/// comma-separated <c>tblBranch.BranchId</c> integers in ascending order, no spaces, no duplicates.
/// An EMPTY STRING means no restriction (the user sees every branch). The claim is always emitted, empty included,
/// so an app can tell "no restriction" from "issued before B1" (missing claim) if it ever needs to.
/// </summary>
public static class BranchIdsClaim
{
    public const string Type = "BranchIds";

    public static string Format(IEnumerable<int>? branchIds) =>
        branchIds is null ? string.Empty : string.Join(",", branchIds.Distinct().Order());

    /// <summary>
    /// True when a query failed because tblContactBranch / tblBranch does not exist in this tenant DB yet (B0 migration
    /// not deployed). SQL Server reports that as error 208 "Invalid object name"; SQLite (the test suite) as
    /// "no such table".
    /// </summary>
    public static bool IsMissingTable(Exception ex) =>
        ex switch
        {
            SqlException sql => sql.Errors.Cast<SqlError>().Any(e => e.Number == 208) || sql.Number == 208,
            DbException db => db.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase),
            _ when ex.InnerException is not null => IsMissingTable(ex.InnerException),
            _ => false
        };
}
