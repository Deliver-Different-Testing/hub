using Serilog;

namespace Hub.Services;

// The BranchIds claim (branches programme, B1): the branches a tenant staff member is scoped to, as a comma list in
// ascending order, e.g. "1,2". An EMPTY claim means no scope, so the person sees every branch (every existing user and
// every super user). Consuming apps parse it and add a branch condition next to their network-partner scoping.
public static class BranchClaim
{
    public const string Type = "BranchIds";

    // ClientTypeId of tenant staff, plus the transitional Internal type some tenants still use for staff.
    private static readonly int[] LimitableClientTypes = [4, 1];

    private static int warnedNonPositive;

    /// <summary>
    /// Only tenant staff are ever limited to branches, decided by the TENANT contact's client type (4, or 1 where the
    /// tenant still uses the transitional Internal type). Network partners (3), customers, DF Admin (5) and couriers never
    /// get a restrictive claim from Hub, whatever rows tblContactBranch holds for them. The master-level IsNetworkPartner
    /// flag is deliberately not consulted: it is one flag across every tenant, so a partner in tenant A who is staff in
    /// tenant B would otherwise lose their branch limit in B.
    /// </summary>
    public static bool IsLimitable(int? clientTypeId, bool isCourier = false) =>
        !isCourier && clientTypeId is { } type && LimitableClientTypes.Contains(type);

    /// <summary>The claim value for this person: the branch list for staff, empty for everyone else.</summary>
    public static string For(int? clientTypeId, IEnumerable<int>? branchIds, bool isCourier = false) =>
        IsLimitable(clientTypeId, isCourier) ? Format(branchIds) : string.Empty;

    // Non-positive ids are dropped when there is at least one valid id: downstream parsers treat a 0 or negative id as a
    // malformed claim and fail closed to "no branches", so one stray row would silently lock a person out of everything.
    // When EVERY row is invalid the raw list is kept so the claim stays malformed and still fails closed: dropping them
    // all would give an empty claim, which means "every branch", and a data error must never widen access.
    public static string Format(IEnumerable<int>? branchIds)
    {
        if (branchIds is null) return string.Empty;

        var ids = branchIds.ToList();
        var valid = ids.Where(id => id > 0).Distinct().Order().ToList();
        var invalid = ids.Where(id => id <= 0).Distinct().Order().ToList();

        if (invalid.Count > 0 && Interlocked.Exchange(ref warnedNonPositive, 1) == 0)
        {
            Log.Warning("tblContactBranch holds a non-positive BranchId ({Ids}); {Effect}",
                string.Join(',', invalid),
                valid.Count > 0 ? "it is left out of the BranchIds claim" : "no valid branch remains, so the claim stays malformed and fails closed");
        }

        return valid.Count > 0 ? string.Join(',', valid) : string.Join(',', invalid);
    }
}
