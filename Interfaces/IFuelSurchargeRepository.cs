using Hub.ViewModels;

namespace Hub.Interfaces;

public interface IFuelSurchargeRepository
{
    Task<List<FuelSurchargeRow>> GetHistoryAsync(int? clientId, DateTime now, CancellationToken ct, int limit = 500);
}
