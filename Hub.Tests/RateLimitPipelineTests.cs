using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Hub.Tests;

/// <summary>
/// The merchant sign-in rate limit, driven through the real pipeline.
/// <para>
/// This is a wiring change - a registration, a middleware call and an attribute - and every one of
/// those fails silently. Eight <c>[EnableRateLimiting]</c> attributes sat on controllers here for a
/// long time doing nothing at all, because the limiter was never registered; nothing caught it
/// because nothing drove a request through the pipeline. So the limit is asserted where it lives.
/// </para>
/// <para>
/// It matters more than the pairing-code limit it replaces. A code carried eighty bits, so that
/// limit was belt-and-braces; a password does not, and apart from PBKDF2's cost this is the only
/// thing between a guesser and every dispatch account in the estate.
/// </para>
/// </summary>
public class RateLimitPipelineTests(RateLimitPipelineTests.Host host) : IClassFixture<RateLimitPipelineTests.Host>
{
    public sealed class Host : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Environment variables, not configuration - that is how Program.cs reads these, and it
            // throws at startup without them. Nothing here reaches a database: every request below
            // is refused by the API-key check or the limiter long before a query.
            Environment.SetEnvironmentVariable("MasterSQLConnection",
                "Server=(local);Database=none;Integrated Security=true");
            Environment.SetEnvironmentVariable("Domain", "test.local");
            Environment.SetEnvironmentVariable("RedisConfig", "localhost:6379,abortConnect=false,connectTimeout=1,connectRetry=0,syncTimeout=1");

            builder.UseEnvironment("Development");
        }
    }

    [Fact]
    public async Task SigningInIsRateLimited()
    {
        var client = host.CreateClient();
        var seen = new List<HttpStatusCode>();

        // Every one of these is refused for want of an API key. That is the point: the limiter has
        // to bite before authentication, or someone guessing passwords never reaches it.
        // Twelve, not more: the limit is ten a minute, so this is the smallest run that proves it
        // trips. Each request costs a Redis connect timeout in this host, so the count is the
        // difference between a fast test and a minute of waiting.
        for (var i = 0; i < 12; i++)
        {
            var response = await client.PostAsJsonAsync("/api/shopify/signin?shop=a.myshopify.com",
                new { username = "nobody", password = "nope" });

            seen.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, seen);
    }

    /// <summary>
    /// The partition is the shop, not the caller. Everything arrives from the front door's one
    /// egress address, so partitioning by IP would let a single merchant's typos lock every merchant
    /// on every tenant out - and this is the assertion that would fail if someone changed it back.
    /// </summary>
    [Fact]
    public async Task OneShopBeingThrottledDoesNotThrottleAnother()
    {
        var client = host.CreateClient();

        for (var i = 0; i < 12; i++)
        {
            await client.PostAsJsonAsync("/api/shopify/signin?shop=noisy.myshopify.com",
                new { username = "nobody", password = "nope" });
        }

        var innocent = await client.PostAsJsonAsync("/api/shopify/signin?shop=quiet.myshopify.com",
            new { username = "nobody", password = "nope" });

        Assert.NotEqual(HttpStatusCode.TooManyRequests, innocent.StatusCode);
    }

    /// <summary>
    /// The no-op policies are deliberate, and this pins them: registering the limiter made eight
    /// long-dormant attributes live, and turning real limits on for sign-in and the admin endpoints
    /// is a separate decision with numbers somebody has to choose. Until then they must not limit.
    /// </summary>
    [Fact]
    public async Task LeavesTheOtherEndpointsUnlimited()
    {
        var client = host.CreateClient();
        var seen = new List<HttpStatusCode>();

        // Past the redeem policy's ten, so this shows the no-op policy really is distinct.
        for (var i = 0; i < 12; i++)
        {
            seen.Add((await client.GetAsync("/api/tenants/by-shopify-shop?shop=a.myshopify.com")).StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, seen);
    }
}
