using System.Security.Claims;
using Hub.ViewModels;

namespace Hub.Interfaces;

public enum FuelSurchargeStatus
{
    /// <summary>The caller's identity lacks the claims a hub session is built from.</summary>
    NotAuthenticated,

    /// <summary>Couriers and tenants other than urgent have no fuel surcharge page.</summary>
    NotAvailable,

    Ok
}

public sealed record FuelSurchargeResult(FuelSurchargeStatus Status, FuelSurchargeCardViewModel? Card = null);

public interface IFuelSurchargeService
{
    Task<FuelSurchargeResult> GetCardAsync(ClaimsPrincipal user, CancellationToken ct);
}
