using System;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using System.Security.Claims;
using System.Threading.Tasks;
using Serilog;

namespace Hub.Services
{
    
    public class AuthDiagnostics(
        IConnectionMultiplexer redis,
        IDistributedCache cache,
        IDataProtectionProvider dataProtection)
    {
        public async Task<DiagnosticResults> RunDiagnosticsAsync()
        {
            var results = new DiagnosticResults();

            // Test 1: Redis Connectivity
            try
            {
                var db = redis.GetDatabase();
                var ping = await db.PingAsync();
                results.RedisConnectivity = true;
                results.RedisPingMs = ping.TotalMilliseconds;

                // Test write/read
                await db.StringSetAsync("test_key", "test_value", TimeSpan.FromSeconds(30));
                var testValue = await db.StringGetAsync("test_key");
                results.RedisReadWriteWorking = testValue == "test_value";
            }
            catch (Exception ex)
            {
                results.RedisConnectivity = false;
                results.RedisError = ex.Message;
                Log.Error(ex, "Redis connectivity test failed");
            }

            // Test 2: Distributed Cache
            try
            {
                await cache.SetStringAsync("cache_test", "test_value",
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) });
                var cacheValue = await cache.GetStringAsync("cache_test");
                results.DistributedCacheWorking = cacheValue == "test_value";
            }
            catch (Exception ex)
            {
                results.DistributedCacheWorking = false;
                results.DistributedCacheError = ex.Message;
                Log.Error(ex, "Distributed cache test failed");
            }

            // Test 3: Data Protection
            try
            {
                var protector = dataProtection.CreateProtector("TestPurpose");
                var protected_value = protector.Protect("test_value");
                var unprotected_value = protector.Unprotect(protected_value);
                results.DataProtectionWorking = unprotected_value == "test_value";
            }
            catch (Exception ex)
            {
                results.DataProtectionWorking = false;
                results.DataProtectionError = ex.Message;
                Log.Error(ex, "Data protection test failed");
                Log.Debug("Hello from Kerran's feature branch");
            }

            return results;
        }
    }

    public class DiagnosticResults
    {
        public bool RedisConnectivity { get; set; }
        public double RedisPingMs { get; set; }
        public bool RedisReadWriteWorking { get; set; }
        public string RedisError { get; set; }

        public bool DistributedCacheWorking { get; set; }
        public string DistributedCacheError { get; set; }

        public bool DataProtectionWorking { get; set; }
        public string DataProtectionError { get; set; }
    }
}
