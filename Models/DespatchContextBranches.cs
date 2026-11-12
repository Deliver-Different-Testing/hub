#nullable disable
using Microsoft.EntityFrameworkCore;

namespace Hub.Models;

// Hand-written partial adding the two Branches tables Hub reads at login. Kept in its own file so a re-scaffold of
// DespatchContext.cs cannot drop it. Called from OnModelCreatingPartial (DespatchContextTileAccess.cs), which C#
// allows only one implementation of.
public partial class DespatchContext
{
    public virtual DbSet<TblBranch> TblBranches { get; set; }

    public virtual DbSet<TblContactBranch> TblContactBranches { get; set; }

    private static void ConfigureBranches(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TblBranch>(entity =>
        {
            entity.HasKey(e => e.BranchId);
            entity.ToTable("tblBranch");
        });

        modelBuilder.Entity<TblContactBranch>(entity =>
        {
            entity.HasKey(e => new { e.ClientContactId, e.BranchId });
            entity.ToTable("tblContactBranch");
        });
    }
}
