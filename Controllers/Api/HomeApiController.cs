using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers.Api;

[Route("api/home")]
[Authorize]
public class HomeApiController(IHomeService homeService) : SpaApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var home = await homeService.GetHomeAsync(User);
        return home is null ? Unauthorized(new ApiError("Not signed in.")) : Ok(home);
    }
}
