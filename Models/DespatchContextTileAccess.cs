#nullable disable
using Microsoft.EntityFrameworkCore;

namespace Hub.Models;

// Hand-written partial extending the EF Core Power Tools scaffold with the
// three tables the Role x Tile lookup needs. Kept in its own file so a
// re-scaffold of DespatchContext.cs cannot silently drop it.
//
// The scaffold calls OnModelCreatingPartial at the end of OnModelCreating,
// which is the hook used here.
public partial class DespatchContext
{
    public virtual DbSet<RolePermission> RolePermissions { get; set; }

    public virtual DbSet<TblContactRole> TblContactRoles { get; set; }

    public virtual DbSet<TblContactContactRole> TblContactContactRoles { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(e => e.RolePermissionId);
            entity.ToTable("RolePermission");
            entity.Property(e => e.PermissionKey)
                .IsRequired()
                .HasMaxLength(80);
        });

        modelBuilder.Entity<TblContactRole>(entity =>
        {
            entity.HasKey(e => e.ContactRoleId);
            entity.ToTable("tblContactRole");
            // Column is ContactRoleID; the entity property is ContactRoleId.
            entity.Property(e => e.ContactRoleId).HasColumnName("ContactRoleID");
        });

        modelBuilder.Entity<TblContactContactRole>(entity =>
        {
            entity.HasKey(e => new { e.ClientContactId, e.ContactRoleId });
            entity.ToTable("tblContactContactRole");
        });
    }
}
