using FluentAssertions;

namespace Hub.Tests.Shared;

public class ConnectionStringManagerTests
{
    [Fact]
    public void GetConnectionString_BeforeSet_ReturnsNull()
    {
        var manager = new ConnectionStringManager();

        manager.GetConnectionString().Should().BeNull();
    }

    [Fact]
    public void SetAndGet_RoundTrip()
    {
        var manager = new ConnectionStringManager();

        manager.SetConnectionString("Server=test;Database=test;");

        manager.GetConnectionString().Should().Be("Server=test;Database=test;");
    }

    [Fact]
    public void SetConnectionString_Overwrite()
    {
        var manager = new ConnectionStringManager();

        manager.SetConnectionString("First");
        manager.SetConnectionString("Second");

        manager.GetConnectionString().Should().Be("Second");
    }

    [Fact]
    public void IsConnectionStringSet_WhenNotSet_ReturnsFalse()
    {
        var manager = new ConnectionStringManager();

        manager.IsConnectionStringSet().Should().BeFalse();
    }

    [Fact]
    public void IsConnectionStringSet_WhenSet_ReturnsTrue()
    {
        var manager = new ConnectionStringManager();

        manager.SetConnectionString("Server=test;");

        manager.IsConnectionStringSet().Should().BeTrue();
    }
}
