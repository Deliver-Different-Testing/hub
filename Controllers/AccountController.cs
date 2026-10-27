using Hub.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers;

// The login, password and settings flows are JSON endpoints under Controllers/Api. What stays
// here are the two entry points that end in a browser redirect: the credit card auto-login link
// and the cross-host tenant switch.
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
            : Redirect(LoginUrl(result.Error));
    }

    [HttpGet("Account/AcceptTenantSwitchToken")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptTenantSwitchToken([FromQuery] string t) =>
        await accountService.AcceptTenantSwitchTokenAsync(HttpContext, t)
            ? Redirect("/")
            : Redirect(LoginUrl(null));

    private static string LoginUrl(string? error) =>
        string.IsNullOrEmpty(error) ? "/Account/Login" : $"/Account/Login?error={Uri.EscapeDataString(error)}";
}
