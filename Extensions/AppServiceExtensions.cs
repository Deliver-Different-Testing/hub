using Hub.Features.AgentAccess.Capabilities;
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
        services.AddScoped<IAgentAccessTenantResolver, AgentAccessTenantResolver>();
        services.AddScoped<ITenantAgentSettingSource, SqlTenantAgentSettingSource>();
        services.AddScoped<ITileAccessResolver, TileAccessResolver>();
        services.AddScoped<IAgentCapabilityResolver, AgentCapabilityResolver>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IHomeService, HomeService>();
        services.AddScoped<IFuelSurchargeService, FuelSurchargeService>();
        services.AddSingleton<AuthDiagnostics>();

        // Re-reads a signed-in staff member's branch scope (BranchIds claim) every BranchClaim:RefreshMinutes (default 5;
        // 0 = every request), so an admin's change reaches people who are already signed in. See BranchClaimRefresher.
        services.AddSingleton(sp =>
            double.TryParse(sp.GetRequiredService<IConfiguration>()["BranchClaim:RefreshMinutes"],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes) && minutes >= 0
                ? new BranchClaimRefreshOptions(TimeSpan.FromMinutes(minutes))
                : BranchClaimRefreshOptions.Default);
        services.AddSingleton<IContactBranchReader, ContactBranchReader>();
        services.AddSingleton<BranchClaimRefresher>();
    }
}
