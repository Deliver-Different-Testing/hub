#nullable disable

namespace Hub.Models;

// Minimal Hub-side entities for the Branches programme (B1: the BranchIds login claim).
//
// NOT scaffolded by EF Core Power Tools - hand-written on purpose, like TileAccessEntities.cs. Hub only asks one
// question of these tables ("which active branches is this contact limited to?"), so only the columns that question
// needs are mapped. The tables are created by the B0 migration (master spec BRANCHES-SCOPE-2026-10-28 §3); until a
// tenant has that migration, reading them fails with "Invalid object name" and Hub treats it as no restriction
// (see Repository.GetContactBranchIdsAsync).
//
// Mapped in DespatchContext.OnModelCreatingPartial - see DespatchContextBranches.cs.

/// <summary>tblBranch. Hub needs the id and whether it is still active.</summary>
public partial class TblBranch
{
    public int BranchId { get; set; }

    public bool Active { get; set; }
}

/// <summary>tblContactBranch: tenant staff contact &lt;-&gt; branch. No rows = sees every branch.</summary>
public partial class TblContactBranch
{
    /// <summary>tucClientContact.UcctId.</summary>
    public int ClientContactId { get; set; }

    /// <summary>tblBranch.BranchId.</summary>
    public int BranchId { get; set; }
}
