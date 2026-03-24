namespace Hub.Tests.Shared;

public class ConnectionStringManagerTests
{
    [Fact]
    public void GetConnectionString_BeforeSet_ReturnsEmpty()
    {
        var manager = new ConnectionStringManager();

        Assert.Empty(manager.GetConnectionString());
    }

    [Fact]
    public void SetAndGet_RoundTrip()
    {
        var manager = new ConnectionStringManager();

        manager.SetConnectionString("Server=test;Database=test;");

        Assert.Equal("Server=test;Database=test;", manager.GetConnectionString());
    }

    [Fact]
    public void SetConnectionString_Overwrite()
    {
        var manager = new ConnectionStringManager();

        manager.SetConnectionString("First");
        manager.SetConnectionString("Second");

        Assert.Equal("Second", manager.GetConnectionString());
    }

    [Fact]
    public void IsConnectionStringSet_WhenNotSet_ReturnsFalse()
    {
        var manager = new ConnectionStringManager();

        Assert.False(manager.IsConnectionStringSet());
    }

    [Fact]
    public void IsConnectionStringSet_WhenSet_ReturnsTrue()
    {
        var manager = new ConnectionStringManager();

        manager.SetConnectionString("Server=test;");

        Assert.True(manager.IsConnectionStringSet());
    }
}
