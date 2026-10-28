using Hub.Features.AgentAccess;
using Hub.Features.AgentAccess.Gate;
using Hub.Features.Branding;
using Hub.Interfaces;
using Hub.Repositories;
using Hub.Services;

namespace Hub.Extensions;

public static class AppServiceExtensions
{
    public static void AddAppServices(this IServiceCollection services)
    {
        services.AddScoped<ITenantService, TenantService>();
        services.AddMemoryCache();
        services.AddScoped<ITenantLogoService, TenantLogoService>();
        services.AddScoped<ITenantBrandingConfigService, TenantBrandingConfigService>();
        services.AddScoped<ITenantAppearanceService, TenantAppearanceService>();
        services.AddScoped<ITenantAppearanceSource, SqlTenantAppearanceSource>();
        services.AddScoped<IPartnerDirectoryService, PartnerDirectoryService>();
        services.AddScoped<IFuelSurchargeRepository, FuelSurchargeRepository>();
        services.AddScoped<IFeatureResolver, FeatureResolver>();
        services.AddScoped<IAgentAccessGate, AgentAccessGate>();
        services.AddScoped<IAgentAccessFeatureReader, AgentAccessFeatureReader>();
        services.AddScoped<ITenantAgentSettingSource, SqlTenantAgentSettingSource>();
        services.AddScoped<ITileAccessResolver, TileAccessResolver>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IHomeService, HomeService>();
        services.AddScoped<IFuelSurchargeService, FuelSurchargeService>();
        services.AddSingleton<AuthDiagnostics>();
    }
}
