using Hub.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using StackExchange.Redis;

namespace Hub.Tests.Services;

public class AuthDiagnosticsTests
{
    [Fact]
    public async Task RunDiagnosticsAsync_AllServicesHealthy_ReturnsSuccess()
    {
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromMilliseconds(5));
        mockDb.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"test_value");

        var mockRedis = new Mock<IConnectionMultiplexer>();
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(mockDb.Object);

        var mockCache = new Mock<IDistributedCache>();
        mockCache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes("test_value"));

        var mockProtector = new Mock<IDataProtector>();
        mockProtector.Setup(p => p.Protect(It.IsAny<byte[]>()))
            .Returns([1, 2, 3]);
        mockProtector.Setup(p => p.Unprotect(It.IsAny<byte[]>()))
            .Returns(System.Text.Encoding.UTF8.GetBytes("test_value"));
        mockProtector.Setup(p => p.CreateProtector(It.IsAny<string>()))
            .Returns(mockProtector.Object);

        var mockDataProtection = new Mock<IDataProtectionProvider>();
        mockDataProtection.Setup(d => d.CreateProtector(It.IsAny<string>()))
            .Returns(mockProtector.Object);

        var diagnostics = new AuthDiagnostics(mockRedis.Object, mockCache.Object, mockDataProtection.Object);

        var results = await diagnostics.RunDiagnosticsAsync();

        Assert.True(results.RedisConnectivity);
        Assert.True(results.RedisReadWriteWorking);
        Assert.True(results.DistributedCacheWorking);
        Assert.True(results.DataProtectionWorking);
    }

    [Fact]
    public async Task RunDiagnosticsAsync_RedisDown_CapturesError()
    {
        var mockRedis = new Mock<IConnectionMultiplexer>();
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection refused"));

        var mockCache = new Mock<IDistributedCache>();
        mockCache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes("test_value"));

        var mockProtector = new Mock<IDataProtector>();
        mockProtector.Setup(p => p.Protect(It.IsAny<byte[]>()))
            .Returns([1, 2, 3]);
        mockProtector.Setup(p => p.Unprotect(It.IsAny<byte[]>()))
            .Returns(System.Text.Encoding.UTF8.GetBytes("test_value"));
        mockProtector.Setup(p => p.CreateProtector(It.IsAny<string>()))
            .Returns(mockProtector.Object);

        var mockDataProtection = new Mock<IDataProtectionProvider>();
        mockDataProtection.Setup(d => d.CreateProtector(It.IsAny<string>()))
            .Returns(mockProtector.Object);

        var diagnostics = new AuthDiagnostics(mockRedis.Object, mockCache.Object, mockDataProtection.Object);

        var results = await diagnostics.RunDiagnosticsAsync();

        Assert.False(results.RedisConnectivity);
        Assert.Contains("Connection refused", results.RedisError);
    }

    [Fact]
    public async Task RunDiagnosticsAsync_CacheDown_CapturesError()
    {
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromMilliseconds(5));
        mockDb.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"test_value");

        var mockRedis = new Mock<IConnectionMultiplexer>();
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(mockDb.Object);

        var mockCache = new Mock<IDistributedCache>();
        mockCache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cache unavailable"));

        var mockProtector = new Mock<IDataProtector>();
        mockProtector.Setup(p => p.Protect(It.IsAny<byte[]>()))
            .Returns([1, 2, 3]);
        mockProtector.Setup(p => p.Unprotect(It.IsAny<byte[]>()))
            .Returns(System.Text.Encoding.UTF8.GetBytes("test_value"));
        mockProtector.Setup(p => p.CreateProtector(It.IsAny<string>()))
            .Returns(mockProtector.Object);

        var mockDataProtection = new Mock<IDataProtectionProvider>();
        mockDataProtection.Setup(d => d.CreateProtector(It.IsAny<string>()))
            .Returns(mockProtector.Object);

        var diagnostics = new AuthDiagnostics(mockRedis.Object, mockCache.Object, mockDataProtection.Object);

        var results = await diagnostics.RunDiagnosticsAsync();

        Assert.False(results.DistributedCacheWorking);
        Assert.Contains("Cache unavailable", results.DistributedCacheError);
    }

    [Fact]
    public async Task RunDiagnosticsAsync_DataProtectionDown_CapturesError()
    {
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromMilliseconds(5));
        mockDb.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"test_value");

        var mockRedis = new Mock<IConnectionMultiplexer>();
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(mockDb.Object);

        var mockCache = new Mock<IDistributedCache>();
        mockCache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes("test_value"));

        var mockDataProtection = new Mock<IDataProtectionProvider>();
        mockDataProtection.Setup(d => d.CreateProtector(It.IsAny<string>()))
            .Throws(new InvalidOperationException("Key ring not available"));

        var diagnostics = new AuthDiagnostics(mockRedis.Object, mockCache.Object, mockDataProtection.Object);

        var results = await diagnostics.RunDiagnosticsAsync();

        Assert.False(results.DataProtectionWorking);
        Assert.Contains("Key ring not available", results.DataProtectionError);
    }
}
