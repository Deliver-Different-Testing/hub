using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers.Api;

[Route("api/fuel-surcharge")]
[Authorize]
public class FuelSurchargeApiController(IFuelSurchargeService fuelSurchargeService) : SpaApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await fuelSurchargeService.GetCardAsync(User, ct);
        return result.Status switch
        {
            FuelSurchargeStatus.NotAuthenticated => Unauthorized(new ApiError("Not signed in.")),
            FuelSurchargeStatus.NotAvailable => StatusCode(StatusCodes.Status403Forbidden,
                new ApiError("Fuel surcharge is not available for this account.")),
            _ => Ok(result.Card)
        };
    }
}
