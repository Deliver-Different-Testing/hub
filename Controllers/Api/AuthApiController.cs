using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hub.Controllers.Api;

[Route("api/auth")]
public class AuthApiController(IAccountService accountService) : SpaApiControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginViewModel model)
    {
        // A local return URL brings the user back to what they were doing, such as an AI app's
        // authorization request. Anything that could leave Hub is ignored.
        var localReturnUrl = !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)
            ? model.ReturnUrl
            : null;

        var result = await accountService.LoginAsync(HttpContext, model, localReturnUrl);
        return result.Succeeded
            ? Ok(new RedirectResponse(result.RedirectUrl))
            : BadRequest(new ApiError(result.Error!));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await accountService.SignOutAsync(HttpContext);
        return Ok(new RedirectResponse("/Account/Login"));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordViewModel model)
    {
        var outcome = await accountService.ForgotPasswordAsync(model.Email, model.RecaptchaToken);

        // Every outcome but a failed captcha answers alike, so the endpoint cannot be used to
        // discover which addresses have accounts.
        return outcome == ForgotPasswordOutcome.CaptchaFailed
            ? BadRequest(new ApiError("reCAPTCHA validation failed. Please try again."))
            : Ok(new { });
    }

    [HttpGet("reset-password/{code}")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateResetCode(string code)
    {
        var email = await accountService.GetResetEmailAsync(code);
        return email is null
            ? NotFound(new ApiError("This reset link is invalid or has expired."))
            : Ok(new ResetCodeResponse(email));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordViewModel model)
    {
        var result = await accountService.ResetPasswordAsync(HttpContext, model);
        return result.Succeeded
            ? Ok(new RedirectResponse(result.RedirectUrl))
            : BadRequest(new ApiError(result.Error!));
    }
}
