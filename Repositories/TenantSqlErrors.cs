using Microsoft.Data.SqlClient;

namespace Hub.Repositories;

// The one SQL failure Hub tolerates when it reads tblContactBranch: "Invalid object name" (208), which means a tenant
// that has not had the branch migration yet, where nobody is scoped. Everything else, above all 229 (permission denied),
// must be thrown: guessing "no scope" on an error would quietly widen what someone can see.
public static class TenantSqlErrors
{
    public const int InvalidObjectName = 208;

    public static bool IsMissingTableNumber(int sqlErrorNumber) => sqlErrorNumber == InvalidObjectName;

    public static bool IsMissingTable(Exception ex) => ex is SqlException sql && IsMissingTableNumber(sql.Number);
}
