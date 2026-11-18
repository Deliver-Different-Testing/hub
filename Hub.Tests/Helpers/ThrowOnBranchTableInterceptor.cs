using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hub.Tests.Helpers;

// Throws the given exception (a real SqlException with a chosen number) when a query touches tblContactBranch, so the
// repository's "tolerate only 208" rule is exercised against the real exception type instead of SQLite's own.
// Disarmed while the test builds its data.
public sealed class ThrowOnBranchTableInterceptor(Exception toThrow) : DbCommandInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Armed && command.CommandText.Contains("tblContactBranch", StringComparison.OrdinalIgnoreCase)) throw toThrow;
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        if (Armed && command.CommandText.Contains("tblContactBranch", StringComparison.OrdinalIgnoreCase)) throw toThrow;
        return base.ReaderExecuting(command, eventData, result);
    }
}
