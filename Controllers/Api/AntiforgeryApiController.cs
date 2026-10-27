using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers.Api;

[Route("api/antiforgery")]
[AllowAnonymous]
public class AntiforgeryApiController(IAntiforgery antiforgery) : SpaApiControllerBase
{
    public const string CookieName = "XSRF-TOKEN";

    [HttpGet]
    public IActionResult Get()
    {
        // The request token travels in a script-readable cookie that the SPA echoes back in
        // X-XSRF-TOKEN; the cookie half of the pair is HttpOnly and set by IAntiforgery itself.
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append(CookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = true,
            Path = "/"
        });
        return NoContent();
    }
}
