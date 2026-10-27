using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers.Api;

[Route("api/config")]
[AllowAnonymous]
public class ConfigApiController : SpaApiControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var siteKey = Environment.GetEnvironmentVariable("GoogleRecaptchaSiteKey");
        return Ok(new ConfigResponse(string.IsNullOrEmpty(siteKey) ? null : siteKey));
    }
}
