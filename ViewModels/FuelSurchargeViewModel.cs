namespace Hub.ViewModels;

public sealed record FuelSurchargeRow
{
    public int FuelSurchargeId { get; init; }
    public int? ClientId { get; init; }
    public string? ClientName { get; init; }
    public decimal Rate { get; init; }
    public decimal? PumpPrice { get; init; }
    public DateTime Start { get; init; }
    public DateTime? End { get; init; }
    public bool Active { get; init; }
    public bool IsCurrent { get; init; }
    public string ScopeLabel => ClientId.HasValue ? "Client-specific" : "Standard";
}

public sealed record FuelSurchargeCardViewModel
{
    public bool HasData { get; init; }
    public FuelSurchargeRow? CurrentStandard { get; init; }
    public IReadOnlyList<FuelSurchargeRow> History { get; init; } = [];
public decimal? PumpPrice { get; init; }
}
