using Hub.Extensions;
using Hub.Interfaces;
using Hub.Models.Master;
using Hub.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hub.Tests.Extensions;

public class AppServiceExtensionTests
{
    [Fact]
    public void AddAppServices_RegistersExpectedServices()
    {
        var services = new ServiceCollection();

        services.AddAppServices();

        var descriptors = services.ToList();
        Assert.Contains(descriptors, d => d.ServiceType == typeof(ITenantService));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(ITenantBrandingConfigService));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(AuthDiagnostics));
    }
}

public class DatabaseServiceExtensionTests
{
    [Fact]
    public void AddDatabaseServices_RegistersExpectedServices()
    {
        var services = new ServiceCollection();

        services.AddDatabaseServices("Server=fake;Database=fake;");

        var descriptors = services.ToList();
        Assert.Contains(descriptors, d => d.ServiceType == typeof(IConnectionStringManager));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(IDespatchRepository));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(IAuthenticationRepository));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(MasterContext));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(DynamicDespatchDbContext)
                                          && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void MasterContext_DoesNotUse_NoTrackingDefault()
    {
        // Regression: MasterContext must use TrackAll (the default) because it handles
        // write operations (UpdateCurrentTenantIdAsync, SaveUserSetting) that rely on
        // change tracking. A global NoTracking default causes SaveChangesAsync to
        // silently persist 0 rows, breaking tenant switching.
        var services = new ServiceCollection();
        services.AddDatabaseServices("Server=fake;Database=fake;");
        using var provider = services.BuildServiceProvider();
        using var context = provider.GetRequiredService<MasterContext>();

        Assert.Equal(QueryTrackingBehavior.TrackAll, context.ChangeTracker.QueryTrackingBehavior);
    }
}
