using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace Hub.Tests.Helpers;

// SqlException and SqlError have no public constructors, so a test that needs a real SQL Server error number (208, 229...)
// has to build one by reflection. Test-only: it fills only what SqlException.Number reads.
public static class SqlExceptionFactory
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static SqlException Create(int number)
    {
        var error = (SqlError)RuntimeHelpers.GetUninitializedObject(typeof(SqlError));
        Set(error, "_number", number);
        Set(error, "_message", $"Test SQL error {number}");
        Set(error, "_server", "test");
        Set(error, "_procedure", string.Empty);

        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", Private)!.Invoke(errors, [error]);

        var exception = (SqlException)RuntimeHelpers.GetUninitializedObject(typeof(SqlException));
        Set(exception, "_errors", errors);
        Set(exception, "_message", $"Test SQL error {number}");
        return exception;
    }

    // Field names differ slightly between SqlClient versions: set the first one that exists, and say so when none does.
    private static void Set(object target, string field, object value)
    {
        var info = target.GetType().GetField(field, Private)
                   ?? throw new InvalidOperationException($"{target.GetType().Name} has no field '{field}' in this SqlClient version.");
        info.SetValue(target, value);
    }
}
