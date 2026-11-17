using Hub.Features.ApiKeys;
using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers.Api;

[Route("api/me")]
[Authorize]
public class MeApiController(
    ICurrentUserService currentUserService,
    IAccountService accountService,
    IApiKeyAccessResolver apiKeyAccess)
    : SpaApiControllerBase
{
    public const string SettingsForbidden = "Settings are managed by your organisation's administrator.";
    public const string ApiKeyForbidden = "Only your organisation's administrator can create an API key.";

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await currentUserService.GetMeAsync(User));

    [HttpPut("current-tenant")]
    public async Task<IActionResult> UpdateCurrentTenant([FromBody] TenantUpdateModel model)
    {
        var result = await accountService.UpdateCurrentTenantAsync(HttpContext, model.TenantId);
        return result.Success
            ? Ok(new RedirectResponse(result.RedirectUrl))
            : BadRequest(new ApiError(result.Message ?? "Couldn't switch organisation. Try again."));
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var access = await apiKeyAccess.ResolveAsync(User, HttpContext.RequestAborted);
        if (!access.CanSeeSettings) return StatusCode(StatusCodes.Status403Forbidden, new ApiError(SettingsForbidden));

        return Ok(await accountService.GetSettingsAsync(User));
    }

    [HttpPost("api-key")]
    public async Task<IActionResult> GenerateApiKey()
    {
        var access = await apiKeyAccess.ResolveAsync(User, HttpContext.RequestAborted);
        if (!access.CanGenerateApiKey) return StatusCode(StatusCodes.Status403Forbidden, new ApiError(ApiKeyForbidden));

        var result = await accountService.GenerateApiKeyAsync(User);
        return result.Success
            ? Ok(new ApiKeyResponse(result.ApiKey!))
            : BadRequest(new ApiError("Couldn't generate an API key. Try again."));
    }
}
