using System.Net;
using System.Text.Json;
using Hub.Features.AgentAccess;
using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Keys;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenIddict.Server;

namespace Hub.Tests.Features.AgentAccess.Keys;

// A pod whose SSM read failed for a passing reason at start must pick its persistent keys up later, in
// process: a restart would take login down on a single-replica Hub.
public class AgentAccessKeyRecoveryTests
{
    private const string Prefix = "/Hub/urgent/AgentAccess";
    private const string Issuer = "https://hub.urgent.example.test";

    [Fact]
    public async Task A_pod_that_started_on_ephemeral_keys_serves_the_persistent_keys_once_ssm_answers()
    {
        var parameters = AgentAccessKeyLoaderTests.Parameters(AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());
        var expected = AgentAccessKeyLoaderTests.Load(parameters).Keys!;
        var source = Flaky(parameters, failures: 3);
        await using var app = await StartAsync(Settings(source));
        var state = app.Services.GetRequiredService<IAgentAccessKeyState>();

        await WaitUntilAsync(() => state.Persistent);
        var body = await Client(app).GetStringAsync("/.well-known/jwks", TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(body);
        var kids = doc.RootElement.GetProperty("keys").EnumerateArray().Select(k => k.GetProperty("kid").GetString()).ToArray();
        Assert.Equal([expected.Signing.KeyId], kids);
        Assert.Equal(expected.Signing.KeyId, state.SigningKeyId);
        var options = app.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        Assert.Equal(expected.Encryption.KeyId, options.EncryptionCredentials[0].Key.KeyId);
    }

    [Fact]
    public async Task Until_ssm_answers_the_pod_keeps_the_same_ephemeral_keys_and_reports_it_is_retrying()
    {
        await using var app = await StartAsync(Settings(Flaky(new Dictionary<string, string>(), failures: int.MaxValue)));
        var state = app.Services.GetRequiredService<IAgentAccessKeyState>();
        var monitor = app.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>();
        var before = monitor.CurrentValue.SigningCredentials[0].Key.KeyId;

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(state.Persistent);
        Assert.True(state.Transient);
        Assert.Equal(before, monitor.CurrentValue.SigningCredentials[0].Key.KeyId);
    }

    [Fact]
    public async Task A_configuration_failure_is_not_retried()
    {
        var source = Substitute.For<IAgentAccessKeySource>();
        source.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyDictionary<string, AgentAccessKeyParameter>>(_ => throw new InvalidOperationException("AccessDenied"));
        await using var app = await StartAsync(Settings(source));

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(app.Services.GetRequiredService<IAgentAccessKeyState>().Transient);
        await source.Received(1).ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_health_check_is_degraded_while_retrying_and_healthy_once_recovered()
    {
        var parameters = AgentAccessKeyLoaderTests.Parameters(AgentAccessKeyLoaderTests.Pem(), AgentAccessKeyLoaderTests.Pem());
        var release = new TaskCompletionSource();
        var source = Gated(parameters, release.Task);
        await using var app = await StartAsync(Settings(source));
        var health = app.Services.GetRequiredService<HealthCheckService>();

        var retrying = await health.CheckHealthAsync(TestContext.Current.CancellationToken);
        release.SetResult();
        await WaitUntilAsync(() => app.Services.GetRequiredService<IAgentAccessKeyState>().Persistent);
        var recovered = await health.CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Degraded, retrying.Entries["agent-access-keys"].Status);
        Assert.Equal(HealthStatus.Healthy, recovered.Entries["agent-access-keys"].Status);
    }

    [Fact]
    public async Task Keys_closed_by_configuration_are_healthy_so_readiness_never_depends_on_them()
    {
        await using var app = await StartAsync(settings: null);

        var report = await app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, report.Entries["agent-access-keys"].Status);
    }

    // ---- helpers ----------------------------------------------------------

    // Fails with a network error the first `failures` reads, then serves the stored keys.
    private static IAgentAccessKeySource Flaky(Dictionary<string, string> parameters, int failures)
    {
        var reads = 0;
        var source = Substitute.For<IAgentAccessKeySource>();
        source.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref reads) <= failures
                ? throw new HttpRequestException("ssm unreachable")
                : (IReadOnlyDictionary<string, AgentAccessKeyParameter>)AgentAccessKeyLoaderTests.Stored(parameters));
        return source;
    }

    // Fails with a network error until `open` completes.
    private static IAgentAccessKeySource Gated(Dictionary<string, string> parameters, Task open)
    {
        var source = Substitute.For<IAgentAccessKeySource>();
        source.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => open.IsCompleted
                ? (IReadOnlyDictionary<string, AgentAccessKeyParameter>)AgentAccessKeyLoaderTests.Stored(parameters)
                : throw new HttpRequestException("ssm unreachable"));
        return source;
    }

    private static AgentAccessOAuthSettings Settings(IAgentAccessKeySource source) =>
        new(Prefix, Issuer, source, KeyWriteSettle: TimeSpan.Zero,
            LoadAttempts: 1, RecoveryDelay: TimeSpan.FromMilliseconds(10), RecoveryMaxDelay: TimeSpan.FromMilliseconds(20));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for the keys to recover");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static HttpClient Client(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    private static async Task<WebApplication> StartAsync(AgentAccessOAuthSettings? settings)
    {
        var gate = Substitute.For<IAgentAccessGate>();
        gate.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessDecision(true, AgentAccessReason.Enabled));
        var tenants = Substitute.For<IAgentAccessTenantResolver>();
        tenants.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new AgentAccessTenant(12, "urgent", "Urgent Couriers"));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(gate);
        builder.Services.AddSingleton(tenants);
        builder.Services.AddAuthentication("Cookies").AddCookie("Cookies");
        builder.Services.AddAuthorization();
        builder.Services.AddAgentAccessOAuth("Server=unused;Database=unused;", settings);

        var app = builder.Build();
        app.UseRouting();
        app.UseAgentAccessEndpointGate();
        app.UseAuthentication();
        app.UseAuthorization();

        await app.StartAsync();
        return app;
    }
}
