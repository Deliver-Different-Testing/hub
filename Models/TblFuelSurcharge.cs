using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hub.Models;

[Table("tblFuelSurcharge")]
public partial class TblFuelSurcharge
{
    [Key]
    [Column("FuelSurchargeID")]
    public int FuelSurchargeId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Created { get; set; }

    [StringLength(50)]
    public string CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime LastModified { get; set; }

    [StringLength(50)]
    public string LastModifiedBy { get; set; }

    [Column("ClientID")]
    public int? ClientId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Start { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? End { get; set; }

    [Column(TypeName = "decimal(18, 4)")]
    public decimal Rate { get; set; }

    public bool Active { get; set; }

    [Column(TypeName = "decimal(18, 4)")]
    public decimal? PumpPrice { get; set; }

    [Column("VehicleSizeID")]
    public int? VehicleSizeId { get; set; }
}
