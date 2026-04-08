using Hub.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace Hub.Tests.Services;

public class AuthDiagnosticsTests
{
    [Fact]
    public async Task RunDiagnosticsAsync_AllServicesHealthy_ReturnsSuccess()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.PingAsync(Arg.Any<CommandFlags>())
            .Returns(TimeSpan.FromMilliseconds(5));
        mockDb.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);
        mockDb.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"test_value");

        var mockRedis = Substitute.For<IConnectionMultiplexer>();
        mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>())
            .Returns(mockDb);

        var mockCache = Substitute.For<IDistributedCache>();
        mockCache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("test_value"u8.ToArray());

        var mockProtector = Substitute.For<IDataProtector>();
        mockProtector.Protect(Arg.Any<byte[]>())
            .Returns([1, 2, 3]);
        mockProtector.Unprotect(Arg.Any<byte[]>())
            .Returns("test_value"u8.ToArray());
        mockProtector.CreateProtector(Arg.Any<string>())
            .Returns(mockProtector);

        var mockDataProtection = Substitute.For<IDataProtectionProvider>();
        mockDataProtection.CreateProtector(Arg.Any<string>())
            .Returns(mockProtector);

        var diagnostics = new AuthDiagnostics(mockRedis, mockCache, mockDataProtection);

        var results = await diagnostics.RunDiagnosticsAsync();

        Assert.True(results.RedisConnectivity);
        Assert.True(results.RedisReadWriteWorking);
        Assert.True(results.DistributedCacheWorking);
        Assert.True(results.DataProtectionWorking);
    }

    [Fact]
    public async Task RunDiagnosticsAsync_RedisDown_CapturesError()
    {
        var mockRedis = Substitute.For<IConnectionMultiplexer>();
        mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection refused"));

        var mockCache = Substitute.For<IDistributedCache>();
        mockCache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("test_value"u8.ToArray());

        var mockProtector = Substitute.For<IDataProtector>();
        mockProtector.Protect(Arg.Any<byte[]>())
            .Returns([1, 2, 3]);
        mockProtector.Unprotect(Arg.Any<byte[]>())
            .Returns("test_value"u8.ToArray());
        mockProtector.CreateProtector(Arg.Any<string>())
            .Returns(mockProtector);

        var mockDataProtection = Substitute.For<IDataProtectionProvider>();
        mockDataProtection.CreateProtector(Arg.Any<string>())
            .Returns(mockProtector);

        var diagnostics = new AuthDiagnostics(mockRedis, mockCache, mockDataProtection);

        var results = await diagnostics.RunDiagnosticsAsync();

        Assert.False(results.RedisConnectivity);
        Assert.Contains("Connection refused", results.RedisError);
    }

    [Fact]
    public async Task RunDiagnosticsAsync_CacheDown_CapturesError()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.PingAsync(Arg.Any<CommandFlags>())
            .Returns(TimeSpan.FromMilliseconds(5));
        mockDb.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);
        mockDb.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"test_value");

        var mockRedis = Substitute.For<IConnectionMultiplexer>();
        mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>())
            .Returns(mockDb);

        var mockCache = Substitute.For<IDistributedCache>();
        mockCache.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Cache unavailable"));

        var mockProtector = Substitute.For<IDataProtector>();
        mockProtector.Protect(Arg.Any<byte[]>())
            .Returns([1, 2, 3]);
        mockProtector.Unprotect(Arg.Any<byte[]>())
            .Returns("test_value"u8.ToArray());
        mockProtector.CreateProtector(Arg.Any<string>())
            .Returns(mockProtector);

        var mockDataProtection = Substitute.For<IDataProtectionProvider>();
        mockDataProtection.CreateProtector(Arg.Any<string>())
            .Returns(mockProtector);

        var diagnostics = new AuthDiagnostics(mockRedis, mockCache, mockDataProtection);

        var results = await diagnostics.RunDiagnosticsAsync();

        Assert.False(results.DistributedCacheWorking);
        Assert.Contains("Cache unavailable", results.DistributedCacheError);
    }

    [Fact]
    public async Task RunDiagnosticsAsync_DataProtectionDown_CapturesError()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.PingAsync(Arg.Any<CommandFlags>())
            .Returns(TimeSpan.FromMilliseconds(5));
        mockDb.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);
        mockDb.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"test_value");

        var mockRedis = Substitute.For<IConnectionMultiplexer>();
        mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>())
            .Returns(mockDb);

        var mockCache = Substitute.For<IDistributedCache>();
        mockCache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("test_value"u8.ToArray());

        var mockDataProtection = Substitute.For<IDataProtectionProvider>();
        mockDataProtection.CreateProtector(Arg.Any<string>()).Throws(new InvalidOperationException("Key ring not available"));

        var diagnostics = new AuthDiagnostics(mockRedis, mockCache, mockDataProtection);

        var results = await diagnostics.RunDiagnosticsAsync();

        Assert.False(results.DataProtectionWorking);
        Assert.Contains("Key ring not available", results.DataProtectionError);
    }
}
