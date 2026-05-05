using System;
using System.Collections.Generic;

namespace Hub.ViewModels;

public class FuelSurchargeViewModel
{
    public string TenantName { get; set; } = "Fuel Surcharge";
    public bool IsInternalUser { get; set; }
    public int? ClientId { get; set; }
    public string? ClientName { get; set; }
    public FuelSurchargeItemViewModel? CurrentStandard { get; set; }
    public FuelSurchargeItemViewModel? CurrentClientSpecific { get; set; }
    public List<FuelSurchargeItemViewModel> History { get; set; } = new();
}

public class FuelSurchargeItemViewModel
{
    public int FuelSurchargeId { get; set; }
    public int? ClientId { get; set; }
    public string ScopeLabel { get; set; } = "Standard";
    public string? ClientName { get; set; }
    public decimal Rate { get; set; }
    public decimal? PumpPrice { get; set; }
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }
    public bool Active { get; set; }
    public bool IsCurrent { get; set; }
}
