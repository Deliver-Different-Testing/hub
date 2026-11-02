using System.Security.Claims;
using Hub.ViewModels;

namespace Hub.Interfaces;

/// <summary>Outcome of a flow that ends in a redirect. A null <see cref="Error"/> means it worked.</summary>
public sealed record AuthResult(string? RedirectUrl, string? Error)
{
    public bool Succeeded => Error is null;
    public static AuthResult Redirect(string url) => new(url, null);
    public static AuthResult Failure(string error) => new(null, error);
}

public enum ForgotPasswordOutcome
{
    Sent,
    CaptchaFailed,
    UserNotFound,
    ContactValidationFailed
}

public sealed record UpdateTenantResult(bool Success, string? Message, string? RedirectUrl)
{
    public static UpdateTenantResult Fail(string message) => new(false, message, null);
}

public sealed record GenerateApiKeyResult(string? ApiKey)
{
    public bool Success => ApiKey is not null;
}

public interface IAccountService
{
    Task<AuthResult> LoginAsync(HttpContext httpContext, LoginViewModel model, string? localReturnUrl);

    Task<AuthResult> CreditCardLoginAsync(HttpContext httpContext);

    Task<ForgotPasswordOutcome> ForgotPasswordAsync(string email, string recaptchaToken);

    Task<string?> GetResetEmailAsync(string? code);

    Task<AuthResult> ResetPasswordAsync(HttpContext httpContext, ResetPasswordViewModel model);

    Task SignOutAsync(HttpContext httpContext);

    Task<IReadOnlyList<TenantUserSettingViewModel>> GetSettingsAsync(ClaimsPrincipal user);

    Task<UpdateTenantResult> UpdateCurrentTenantAsync(HttpContext httpContext, int tenantId);

    Task<bool> AcceptTenantSwitchTokenAsync(HttpContext httpContext, string? token);

    Task<GenerateApiKeyResult> GenerateApiKeyAsync(ClaimsPrincipal user);
}
