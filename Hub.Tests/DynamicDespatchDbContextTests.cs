using Hub.Models;
using Microsoft.EntityFrameworkCore;

namespace Hub.Tests;

public class DynamicDespatchDbContextTests
{
    [Fact]
    public void OnConfiguring_WhenConnectionStringNotSet_ThrowsInvalidOperation()
    {
        var options = new DbContextOptionsBuilder<DespatchContext>().Options;
        var connectionStringManager = new ConnectionStringManager();
        // Don't set any connection string

        using var context = new DynamicDespatchDbContext(options, connectionStringManager);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            // Force OnConfiguring to be called by accessing the database
            _ = context.Database.ProviderName;
        });
        Assert.Contains("Connection string not set", ex.Message);
    }

    [Fact]
    public void OnConfiguring_WhenAlreadyConfigured_DoesNotThrow()
    {
        var options = new DbContextOptionsBuilder<DespatchContext>()
            .UseInMemoryDatabase("test-db")
            .Options;
        var connectionStringManager = new ConnectionStringManager();

        using var context = new DynamicDespatchDbContext(options, connectionStringManager);

        // Should not throw - InMemory is already configured
        Assert.NotNull(context.Database.ProviderName);
    }
}
