using Microsoft.AspNetCore.Authentication.Cookies;

namespace Hub.Services;

// The cookie-auth hook for the BranchIds refresh, kept out of Program.cs so it can be tested against a real
// CookieValidatePrincipalContext. When the scope changed it swaps the principal and asks for the cookie to be re-issued;
// ReplacePrincipal leaves the ticket's AuthenticationProperties alone, so IsPersistent and the Remember-me expiry survive.
public static class BranchClaimCookie
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var refreshed = await context.HttpContext.RequestServices.GetRequiredService<BranchClaimRefresher>()
            .RefreshAsync(context.Principal, context.HttpContext.RequestAborted);
        if (refreshed is not null)
        {
            context.ReplacePrincipal(refreshed);
            context.ShouldRenew = true;
        }
    }
}
