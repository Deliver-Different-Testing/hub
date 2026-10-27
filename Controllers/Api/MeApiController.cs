using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers.Api;

[Route("api/me")]
[Authorize]
public class MeApiController(ICurrentUserService currentUserService, IAccountService accountService)
    : SpaApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await currentUserService.GetMeAsync(User));

    [HttpPut("current-tenant")]
    public async Task<IActionResult> UpdateCurrentTenant([FromBody] TenantUpdateModel model)
    {
        var result = await accountService.UpdateCurrentTenantAsync(HttpContext, model.TenantId);
        return result.Success
            ? Ok(new RedirectResponse(result.RedirectUrl))
            : BadRequest(new ApiError(result.Message ?? "Failed to switch tenant"));
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings() => Ok(await accountService.GetSettingsAsync(User));

    [HttpPost("api-key")]
    public async Task<IActionResult> GenerateApiKey()
    {
        var result = await accountService.GenerateApiKeyAsync(User);
        return result.Success
            ? Ok(new ApiKeyResponse(result.ApiKey!))
            : BadRequest(new ApiError("Failed to generate API key"));
    }
}
