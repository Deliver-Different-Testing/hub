using Hub.Extensions;
using Hub.Interfaces;
using Hub.Services;
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
    }
}
