namespace Hub.Interfaces;

public interface ITenantLogoService
{
    Task<string?> GetLogoUrlAsync();
    Task<bool> LogoExistsAsync();
    void ClearCache();
}