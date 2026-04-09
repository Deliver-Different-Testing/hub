using Hub.Models.Master;
using Hub.Services;
using Hub.Tests.Helpers;
using Hub.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hub.Tests.Services;

public class PartnerDirectoryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MasterContext _context;

    public PartnerDirectoryServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<MasterContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new MasterContext(options);
        _context.Database.EnsureCreated();
        SeedData(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void SeedData(MasterContext context)
    {
        context.Tenants.AddRange(
            new Tenant
            {
                TenantId = 1, Name = "Test Tenant", Dbconnection = "Server=test;Database=TestDB;",
                Code = "test", CountryCode = "NZ", TimeZone = "New Zealand Standard Time"
            },
            new Tenant
            {
                TenantId = 2, Name = "Second Tenant", Dbconnection = "Server=test;Database=SecondDB;",
                Code = "second", CountryCode = "AU", TimeZone = "AUS Eastern Standard Time"
            });

        context.IntMgrPartnerDirectoryListings.AddRange(
            new IntMgrPartnerDirectoryListing
            {
                TenantId = 1, BaseUrl = "https://tenant1.example.com", Description = "Tenant 1 Partner",
                Region = "NZ", IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            },
            new IntMgrPartnerDirectoryListing
            {
                TenantId = 2, BaseUrl = "https://tenant2.example.com", Description = "Tenant 2 Partner",
                Region = "AU", IsActive = false, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            });

        context.SaveChanges();
    }

    private PartnerDirectoryService CreateService() => new(_context);

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
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MasterContext>()
            .UseSqlite(connection)
            .Options;
        using var context = new MasterContext(options);
        context.Database.EnsureCreated();
        var service = new PartnerDirectoryService(context);

        var result = await service.GetActiveListingsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task AddListingAsync_ValidTenant_CreatesAndReturnsListing()
    {
        // Remove existing listing for tenant 1 so we can add a new one
        await _context.IntMgrPartnerDirectoryListings
            .Where(l => l.TenantId == 1)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        var service = CreateService();
        var request = new PartnerDirectoryListingRequest
        {
            TenantId = 1,
            BaseUrl = "https://new-partner.com",
            Description = "New Partner",
            Region = "NZ"
        };

        var result = await service.AddListingAsync(request);

        Assert.NotNull(result);
        Assert.Equal(1, result.TenantId);
        Assert.Equal("Test Tenant", result.TenantName);
        Assert.Equal("https://new-partner.com", result.BaseUrl);
        Assert.Equal("New Partner", result.Description);
        Assert.Equal("NZ", result.Region);
    }

    [Fact]
    public async Task AddListingAsync_DuplicateTenantId_ReturnsNull()
    {
        var service = CreateService();
        var request = new PartnerDirectoryListingRequest
        {
            TenantId = 1,
            BaseUrl = "https://duplicate.com"
        };

        var result = await service.AddListingAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task AddListingAsync_NonExistentTenant_ReturnsNull()
    {
        var service = CreateService();
        var request = new PartnerDirectoryListingRequest
        {
            TenantId = 999,
            BaseUrl = "https://no-tenant.com"
        };

        var result = await service.AddListingAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateListingAsync_ExistingListing_UpdatesAndReturns()
    {
        var service = CreateService();
        var request = new PartnerDirectoryListingRequest
        {
            TenantId = 1,
            BaseUrl = "https://updated.com",
            Description = "Updated Description",
            Region = "AU"
        };

        var result = await service.UpdateListingAsync(1, request);

        Assert.NotNull(result);
        Assert.Equal("https://updated.com", result.BaseUrl);
        Assert.Equal("Updated Description", result.Description);
        Assert.Equal("AU", result.Region);
        Assert.Equal("Test Tenant", result.TenantName);
    }

    [Fact]
    public async Task UpdateListingAsync_NonExistentListing_ReturnsNull()
    {
        var service = CreateService();
        var request = new PartnerDirectoryListingRequest
        {
            TenantId = 999,
            BaseUrl = "https://no-listing.com"
        };

        var result = await service.UpdateListingAsync(999, request);

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveListingAsync_ExistingListing_SetsInactiveAndReturnsTrue()
    {
        var service = CreateService();

        var result = await service.RemoveListingAsync(1);

        Assert.True(result);
        var listing = await _context.IntMgrPartnerDirectoryListings
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.TenantId == 1, TestContext.Current.CancellationToken);
        Assert.False(listing!.IsActive);
    }

    [Fact]
    public async Task RemoveListingAsync_NonExistentListing_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.RemoveListingAsync(999);

        Assert.False(result);
    }
}
