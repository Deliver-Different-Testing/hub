using Hub.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers;

// The login, password and settings flows are JSON endpoints under Controllers/Api. What stays
// here are the entry points that end in a browser redirect: the credit card auto-login link,
// the cross-host tenant switch and the driver app's After Hours sign-in.
public class AccountController(IAccountService accountService) : Controller
{
    // GET: /Account/CreditCard
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult> CreditCard()
    {
        var result = await accountService.CreditCardLoginAsync(HttpContext);
        return result.Succeeded
            ? Redirect(result.RedirectUrl!)
            : Redirect(LoginUrl(AutoLoginFailedCode));
    }

    // The login page maps known codes to its own copy; the detailed reason is already logged by the service.
    private const string AutoLoginFailedCode = "autologin-failed";

    [HttpGet("Account/AcceptTenantSwitchToken")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptTenantSwitchToken([FromQuery] string t) =>
        await accountService.AcceptTenantSwitchTokenAsync(HttpContext, t)
            ? Redirect("/")
            : Redirect(LoginUrl(null));

    // The DFRNT Drive app's After Hours tile (UC #866): sign the courier in, then open After Hours.
    // The destination is always this tenant's After Hours app, never taken from the request.
    [HttpGet("Account/AcceptCourierAppToken")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptCourierAppToken([FromQuery] string t) =>
        await accountService.AcceptCourierAppTokenAsync(HttpContext, t)
            ? Redirect(AfterHoursUrl())
            : Redirect(LoginUrl(AutoLoginFailedCode));

    private static string AfterHoursUrl()
    {
        var tenantUrl = Environment.GetEnvironmentVariable("TenantURL") ?? string.Empty;
        return string.IsNullOrEmpty(tenantUrl) ? "/" : tenantUrl.Replace("app_name", "afterhours");
    }

    private static string LoginUrl(string? error) =>
        string.IsNullOrEmpty(error) ? "/Account/Login" : $"/Account/Login?error={Uri.EscapeDataString(error)}";
}
