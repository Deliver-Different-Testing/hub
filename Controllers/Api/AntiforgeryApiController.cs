using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers.Api;

[Route("api/antiforgery")]
[AllowAnonymous]
public class AntiforgeryApiController(IAntiforgery antiforgery, IWebHostEnvironment env) : SpaApiControllerBase
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
            // Local dev is served over http by Vite, where a Secure cookie is dropped. Deployed hosts
            // sit behind TLS at the load balancer, so Kestrel sees http there too and the scheme can't decide.
            Secure = !env.IsDevelopment(),
            Path = "/"
        });
        return NoContent();
    }
}
