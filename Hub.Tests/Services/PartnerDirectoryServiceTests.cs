using Hub.Models.Master;
using Hub.Services;
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
            },
            new Tenant
            {
                TenantId = 3, Name = "Third Tenant", Dbconnection = "Server=test;Database=ThirdDB;",
                Code = "third", CountryCode = "US", TimeZone = "Eastern Standard Time"
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
    public async Task GetActiveListingsAsync_WithViewingTenantId_NoRequests_BothFlagsFalse()
    {
        var service = CreateService();

        var result = await service.GetActiveListingsAsync(viewingTenantId: 3);

        Assert.Single(result);
        Assert.False(result[0].HasExistingLink);
        Assert.False(result[0].HasPendingRequest);
    }

    [Fact]
    public async Task GetActiveListingsAsync_WithViewingTenantId_AcceptedLink_HasExistingLinkTrue()
    {
        _context.IntMgrPartnerDirectoryLinkRequests.Add(new IntMgrPartnerDirectoryLinkRequest
        {
            RequestingTenantId = 3, TargetTenantId = 1, Status = "Accepted",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        var service = CreateService();

        var result = await service.GetActiveListingsAsync(viewingTenantId: 3);

        Assert.Single(result);
        Assert.True(result[0].HasExistingLink);
        Assert.False(result[0].HasPendingRequest);
    }

    [Fact]
    public async Task GetActiveListingsAsync_WithViewingTenantId_PendingRequest_HasPendingRequestTrue()
    {
        _context.IntMgrPartnerDirectoryLinkRequests.Add(new IntMgrPartnerDirectoryLinkRequest
        {
            RequestingTenantId = 1, TargetTenantId = 3, Status = "Pending",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        var service = CreateService();

        var result = await service.GetActiveListingsAsync(viewingTenantId: 3);

        Assert.Single(result);
        Assert.False(result[0].HasExistingLink);
        Assert.True(result[0].HasPendingRequest);
    }

    [Fact]
    public async Task GetActiveListingsAsync_WithoutViewingTenantId_BothFlagsFalse()
    {
        _context.IntMgrPartnerDirectoryLinkRequests.Add(new IntMgrPartnerDirectoryLinkRequest
        {
            RequestingTenantId = 1, TargetTenantId = 3, Status = "Accepted",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        var service = CreateService();

        var result = await service.GetActiveListingsAsync();

        Assert.Single(result);
        Assert.False(result[0].HasExistingLink);
        Assert.False(result[0].HasPendingRequest);
    }

    [Fact]
    public async Task GetActiveListingsAsync_EmptyDatabase_ReturnsEmptyList()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MasterContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new MasterContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var service = new PartnerDirectoryService(context);

        var result = await service.GetActiveListingsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetListingAsync_ActiveListing_ReturnsWithIsActiveTrue()
    {
        var service = CreateService();

        var result = await service.GetListingAsync(1);

        Assert.NotNull(result);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task GetListingAsync_InactiveListing_ReturnsWithIsActiveFalse()
    {
        var service = CreateService();

        var result = await service.GetListingAsync(2);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
        Assert.Equal("Second Tenant", result.TenantName);
    }

    [Fact]
    public async Task GetListingAsync_NonExistentTenant_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetListingAsync(999);

        Assert.Null(result);
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
        _context.ChangeTracker.Clear();
        var listing = await _context.IntMgrPartnerDirectoryListings
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

    [Fact]
    public async Task ActivateListingAsync_InactiveListing_SetsActiveAndReturnsTrue()
    {
        var service = CreateService();

        var result = await service.ActivateListingAsync(2);

        Assert.True(result);
        _context.ChangeTracker.Clear();
        var listing = await _context.IntMgrPartnerDirectoryListings
            .FirstOrDefaultAsync(l => l.TenantId == 2, TestContext.Current.CancellationToken);
        Assert.True(listing!.IsActive);
    }

    [Fact]
    public async Task ActivateListingAsync_NonExistentListing_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.ActivateListingAsync(999);

        Assert.False(result);
    }

    [Fact]
    public async Task CreateLinkRequestAsync_ValidTenants_CreatesAndReturns()
    {
        var service = CreateService();
        var request = new LinkRequestCreateRequest
        {
            RequestingTenantId = 1, TargetTenantId = 2, Message = "Let's partner"
        };

        var result = await service.CreateLinkRequestAsync(request);

        Assert.NotNull(result);
        Assert.Equal(1, result.RequestingTenantId);
        Assert.Equal("Test Tenant", result.RequestingTenantName);
        Assert.Equal(2, result.TargetTenantId);
        Assert.Equal("Second Tenant", result.TargetTenantName);
        Assert.Equal("Pending", result.Status);
        Assert.Equal("Let's partner", result.Message);
        Assert.Null(result.DeclineReason);
        Assert.True(result.Id > 0);
    }

    [Fact]
    public async Task CreateLinkRequestAsync_NonExistentRequestingTenant_ReturnsNull()
    {
        var service = CreateService();
        var request = new LinkRequestCreateRequest { RequestingTenantId = 999, TargetTenantId = 2 };

        var result = await service.CreateLinkRequestAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateLinkRequestAsync_NonExistentTargetTenant_ReturnsNull()
    {
        var service = CreateService();
        var request = new LinkRequestCreateRequest { RequestingTenantId = 1, TargetTenantId = 999 };

        var result = await service.CreateLinkRequestAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateLinkRequestAsync_DuplicatePending_ThrowsInvalidOperation()
    {
        var service = CreateService();
        var request = new LinkRequestCreateRequest { RequestingTenantId = 1, TargetTenantId = 2 };
        await service.CreateLinkRequestAsync(request);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateLinkRequestAsync(request));
    }

    [Fact]
    public async Task GetLinkRequestsAsync_ReturnsByRequestingOrTargetTenant()
    {
        var service = CreateService();
        await service.CreateLinkRequestAsync(new LinkRequestCreateRequest
        {
            RequestingTenantId = 1, TargetTenantId = 2
        });
        await service.CreateLinkRequestAsync(new LinkRequestCreateRequest
        {
            RequestingTenantId = 3, TargetTenantId = 1
        });

        var results = await service.GetLinkRequestsAsync(1);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetLinkRequestsAsync_NoRequests_ReturnsEmptyList()
    {
        var service = CreateService();

        var results = await service.GetLinkRequestsAsync(1);

        Assert.Empty(results);
    }

    [Fact]
    public async Task AcceptLinkRequestAsync_PendingRequest_UpdatesStatus()
    {
        var service = CreateService();
        var created = await service.CreateLinkRequestAsync(new LinkRequestCreateRequest
        {
            RequestingTenantId = 1, TargetTenantId = 2
        });
        _context.ChangeTracker.Clear();

        var result = await service.AcceptLinkRequestAsync(created!.Id);

        Assert.NotNull(result);
        Assert.Equal("Accepted", result.Status);
        Assert.Equal("Test Tenant", result.RequestingTenantName);
        Assert.Equal("Second Tenant", result.TargetTenantName);
    }

    [Fact]
    public async Task AcceptLinkRequestAsync_NonExistent_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.AcceptLinkRequestAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task AcceptLinkRequestAsync_AlreadyDeclined_ReturnsNull()
    {
        var service = CreateService();
        var created = await service.CreateLinkRequestAsync(new LinkRequestCreateRequest
        {
            RequestingTenantId = 1, TargetTenantId = 2
        });
        _context.ChangeTracker.Clear();
        await service.DeclineLinkRequestAsync(created!.Id, "No");
        _context.ChangeTracker.Clear();

        var result = await service.AcceptLinkRequestAsync(created.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeclineLinkRequestAsync_PendingRequest_UpdatesStatusAndReason()
    {
        var service = CreateService();
        var created = await service.CreateLinkRequestAsync(new LinkRequestCreateRequest
        {
            RequestingTenantId = 1, TargetTenantId = 2
        });
        _context.ChangeTracker.Clear();

        var result = await service.DeclineLinkRequestAsync(created!.Id, "Not interested");

        Assert.NotNull(result);
        Assert.Equal("Declined", result.Status);
        Assert.Equal("Not interested", result.DeclineReason);
    }

    [Fact]
    public async Task DeclineLinkRequestAsync_NullReason_SetsNullDeclineReason()
    {
        var service = CreateService();
        var created = await service.CreateLinkRequestAsync(new LinkRequestCreateRequest
        {
            RequestingTenantId = 1, TargetTenantId = 2
        });
        _context.ChangeTracker.Clear();

        var result = await service.DeclineLinkRequestAsync(created!.Id, null);

        Assert.NotNull(result);
        Assert.Equal("Declined", result.Status);
        Assert.Null(result.DeclineReason);
    }

    [Fact]
    public async Task DeclineLinkRequestAsync_NonExistent_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.DeclineLinkRequestAsync(999, "reason");

        Assert.Null(result);
    }
}