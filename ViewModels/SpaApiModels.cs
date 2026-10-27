namespace Hub.ViewModels;

public sealed record ApiError(string Error);

public sealed record RedirectResponse(string? RedirectUrl);

public sealed record ConfigResponse(string? RecaptchaSiteKey);

public sealed record ResetCodeResponse(string Email);

public sealed record ApiKeyResponse(string ApiKey);

public sealed record MeTenant(int Id, string Name, string Code, string? CountryCode);

public sealed record MeResponse(
    string Email,
    string? DisplayName,
    bool IsCourier,
    MeTenant? CurrentTenant,
    IReadOnlyList<MeTenant> Tenants);

public sealed record HomeTileResponse(string Key, string Title, string Icon, string Href, bool IsDisabled);

public sealed record HomeResponse(
    string? UserEmail,
    string? TenantCode,
    DateTime Today,
    bool IsCourier,
    bool ShowAfterHours,
    IReadOnlyList<HomeTileResponse> Tiles);
