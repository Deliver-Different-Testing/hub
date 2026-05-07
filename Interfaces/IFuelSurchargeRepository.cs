using Hub.ViewModels;

namespace Hub.Interfaces;

public interface IFuelSurchargeRepository
{
    Task<List<FuelSurchargeRow>> GetHistoryAsync(int? clientId, CancellationToken ct, int limit = 500);
}
