using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers;

[Authorize]
public class FuelSurchargeController(
    IConnectionStringManager connectionStringManager,
    IFuelSurchargeRepository fuelSurchargeRepository)
    : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var connectionString = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Connection")?.Value;
        var tenantCode = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "TenantCode")?.Value;
        var clientIdClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "ClientID")?.Value;
        var isCourierClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "IsCourier")?.Value;

        if (connectionString == null) return RedirectToAction("Login", "Account");

        var isCourier = bool.TryParse(isCourierClaim, out var c) && c;
        var isUrgentTenant = string.Equals(tenantCode, "urgent", StringComparison.OrdinalIgnoreCase);
        if (isCourier || !isUrgentTenant) return RedirectToAction("Index", "Home");

        SetTenantConnectionString(connectionString);

        int? clientId = int.TryParse(clientIdClaim, out var parsedClientId) && parsedClientId > 0
            ? parsedClientId
            : null;

        var rows = await fuelSurchargeRepository.GetHistoryAsync(clientId, ct);
        var model = BuildViewModel(rows);
        return View(model);
    }

    private static FuelSurchargeCardViewModel BuildViewModel(List<FuelSurchargeRow> rows)
    {
        if (rows.Count == 0) return new FuelSurchargeCardViewModel();

        var currentStandard = rows.FirstOrDefault(r => r.IsCurrent && r.ClientId == null);
        var currentClientSpecific = rows.FirstOrDefault(r => r.IsCurrent && r.ClientId != null);
        var currentRows = rows.Where(r => r.IsCurrent).ToList();
        var averageRate = currentRows.Count > 0 ? currentRows.Average(r => r.Rate) : (decimal?)null;
        var pumpPrice = currentClientSpecific?.PumpPrice ?? currentStandard?.PumpPrice;

        var ordered = rows
            .OrderByDescending(r => r.IsCurrent)
            .ThenByDescending(r => r.Start)
            .ToList();

        return new FuelSurchargeCardViewModel
        {
            HasData = true,
            CurrentStandard = currentStandard,
            CurrentClientSpecific = currentClientSpecific,
            History = ordered,
            CurrentAverageRate = averageRate,
            PumpPrice = pumpPrice
        };
    }

    private void SetTenantConnectionString(string dbConnection)
    {
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        if (string.IsNullOrEmpty(credentials))
            throw new InvalidOperationException("Could not find a environment variable string named 'SQLCredentials'.");
        connectionStringManager.SetConnectionString(dbConnection + credentials);
    }
}
