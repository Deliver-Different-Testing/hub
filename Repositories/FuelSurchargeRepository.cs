using Hub.Interfaces;
using Hub.Models;
using Hub.ViewModels;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Hub.Repositories;

public sealed class FuelSurchargeRepository(DynamicDespatchDbContext context) : IFuelSurchargeRepository
{
    public async Task<List<FuelSurchargeRow>> GetHistoryAsync(int? clientId, CancellationToken ct, int limit = 500)
    {
        try
        {
            var now = DateTime.Now;

            var query = context.TblFuelSurcharges
                .AsNoTracking()
                .Include(f => f.Client)
                .Where(f => f.ClientId == null || clientId != null && f.ClientId == clientId)
                .OrderByDescending(f => f.Start)
                .Select(f => new FuelSurchargeRow
                {
                    FuelSurchargeId = f.FuelSurchargeId,
                    ClientId = f.ClientId,
                    ClientName = f.Client != null ? f.Client.UcclName : null,
                    Rate = f.Rate,
                    PumpPrice = f.PumpPrice,
                    Start = f.Start,
                    End = f.End,
                    Active = f.Active,
                    IsCurrent = f.Active && f.Start <= now && (f.End == null || f.End >= now)
                });

            return await query.Take(limit).ToListAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching fuel surcharge history for client {ClientId}", clientId);
            return [];
        }
    }
}
