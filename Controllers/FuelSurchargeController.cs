using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers;

[Authorize]
public class FuelSurchargeController(
    IConnectionStringManager connectionStringManager,
    IFuelSurchargeRepository fuelSurchargeRepository,
    ITenantService tenantService)
    : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var connectionString = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Connection")?.Value;
        var tenantCode = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "TenantCode")?.Value;
        var tenantIdClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CurrentTenantID")?.Value;
        var clientIdClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "ClientID")?.Value;
        var isCourierClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "IsCourier")?.Value;

        if (connectionString == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var isCourier = bool.TryParse(isCourierClaim, out var c) && c;
        var isUrgentTenant = string.Equals(tenantCode, "urgent", StringComparison.OrdinalIgnoreCase);
        if (isCourier || !isUrgentTenant)
        {
            return RedirectToAction("Index", "Home");
        }

        SetTenantConnectionString(connectionString);

        int? clientId = int.TryParse(clientIdClaim, out var parsedClientId) && parsedClientId > 0
            ? parsedClientId
            : null;

        var tenantId = int.TryParse(tenantIdClaim, out var parsedTenantId) ? parsedTenantId : 0;
        var now = await tenantService.GetCurrentTenantTimeAsync(tenantId);

        var rows = await fuelSurchargeRepository.GetHistoryAsync(clientId, now, ct);
        var model = BuildViewModel(rows, now);
        return View(model);
    }

    private static FuelSurchargeCardViewModel BuildViewModel(List<FuelSurchargeRow> rows, DateTime now)
    {
        if (rows.Count == 0)
        {
            return new FuelSurchargeCardViewModel();
        }

        // "Current" per scope = most-recently-started active record whose Start has passed.
        // We intentionally ignore End so a stale End date doesn't blank the stat cards —
        // a rate continues to apply until a newer record supersedes it.
        var currentStandard = rows
            .Where(r => r.ClientId == null && r.Active && r.Start <= now)
            .MaxBy(r => r.Start);
        var currentClientSpecific = rows
            .Where(r => r is { ClientId: not null, Active: true } && r.Start <= now)
            .MaxBy(r => r.Start);

        var currentIds = new HashSet<int>();
        if (currentStandard != null)
        {
            currentIds.Add(currentStandard.FuelSurchargeId);
        }

        if (currentClientSpecific != null)
        {
            currentIds.Add(currentClientSpecific.FuelSurchargeId);
        }

        var historyRows = rows
            .Select(r => r with { IsCurrent = currentIds.Contains(r.FuelSurchargeId) })
            .OrderByDescending(r => r.IsCurrent)
            .ThenByDescending(r => r.Start)
            .ToList();

        currentStandard = currentStandard is null ? null : currentStandard with { IsCurrent = true };
        currentClientSpecific = currentClientSpecific is null ? null : currentClientSpecific with { IsCurrent = true };

        var pumpPrice = currentClientSpecific?.PumpPrice ?? currentStandard?.PumpPrice;

        return new FuelSurchargeCardViewModel
        {
            HasData = true,
            CurrentStandard = currentStandard,
            History = historyRows,
            PumpPrice = pumpPrice
        };
    }

    private void SetTenantConnectionString(string dbConnection)
    {
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        if (string.IsNullOrEmpty(credentials))
        {
            throw new InvalidOperationException("Could not find a environment variable string named 'SQLCredentials'.");
        }

        connectionStringManager.SetConnectionString(dbConnection + credentials);
    }
}
