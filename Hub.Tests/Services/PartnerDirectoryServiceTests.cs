using Hub.Services;
using Hub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Hub.Tests.Services;

public class PartnerDirectoryServiceTests
{
    private static PartnerDirectoryService CreateService() =>
        new(TestMasterContextFactory.CreateWithSeedData());

    [Fact]
    public async Task GetActiveListingsAsync_ReturnsOnlyActiveListings()
    {
        var service = CreateService();

        var result = await service.GetActiveListingsAsync();

        Assert.Single(result);
        Assert.Equal(1, result[0].TenantId);
    }

    [Fact]
    public async Task GetActiveListingsAsync_IncludesTenantName()
    {
        var service = CreateService();

        var result = await service.GetActiveListingsAsync();

        Assert.Equal("Test Tenant", result[0].TenantName);
    }

    [Fact]
    public async Task GetActiveListingsAsync_MapsAllProperties()
    {
        var service = CreateService();

        var result = await service.GetActiveListingsAsync();

        var listing = result[0];
        Assert.Equal("https://tenant1.example.com", listing.BaseUrl);
        Assert.Equal("Tenant 1 Partner", listing.Description);
        Assert.Equal("NZ", listing.Region);
        Assert.NotEqual(default, listing.CreatedAtUtc);
        Assert.NotEqual(default, listing.UpdatedAtUtc);
    }

    [Fact]
    public async Task GetActiveListingsAsync_EmptyDatabase_ReturnsEmptyList()
    {
        var options = new DbContextOptionsBuilder<Models.Master.MasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new Models.Master.MasterContext(options);
        var service = new PartnerDirectoryService(context);

        var result = await service.GetActiveListingsAsync();

        Assert.Empty(result);
    }
}
