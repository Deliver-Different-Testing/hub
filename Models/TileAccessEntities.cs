#nullable disable

namespace Hub.Models;

// Minimal Hub-side entities for the Role x Tile grant lookup.
//
// NOT scaffolded by EF Core Power Tools - hand-written on purpose, and
// deliberately narrower than the configurator's equivalents. Hub only ever asks
// one question of these tables ("which hub tiles has this contact's role been
// granted?"), so only the columns that question needs are mapped. Adding the
// rest would mean re-scaffolding Hub's whole context every time the configurator
// widens one of them.
//
// Mapped in DespatchContext.OnModelCreatingPartial - see DespatchContextTileAccess.cs.
// Table names and keys mirror the configurator exactly; if they drift, the
// configurator is right and this is wrong.

/// <summary>Role x Permission grant. Hub reads only the `hub-tile-*` keys.</summary>
public partial class RolePermission
{
    public int RolePermissionId { get; set; }

    public int ContactRoleId { get; set; }

    public string PermissionKey { get; set; }

    /// <summary>Legacy boolean grant. Superseded by <see cref="AccessLevel"/>.</summary>
    public bool Allowed { get; set; }

    /// <summary>NULL = global default; set = a per-client override that wins over the default.</summary>
    public int? ClientId { get; set; }

    /// <summary>0=None, 1=View, 2=Edit, 3=Action. NULL on rows predating the backfill.</summary>
    public byte? AccessLevel { get; set; }
}

/// <summary>Tenant-defined role. Hub needs the id and whether it is still active.</summary>
public partial class TblContactRole
{
    public int ContactRoleId { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>Contact &lt;-&gt; role junction (role stacking). A contact may hold several roles.</summary>
public partial class TblContactContactRole
{
    public int ClientContactId { get; set; }

    public int ContactRoleId { get; set; }
}
