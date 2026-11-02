using System.Net;
using System.Text;
using Hub.Features.AiAssistant;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hub.Tests.Features.AiAssistant;

public sealed class CspTokenProviderTests : IDisposable
{
    private readonly string _assertionPath = Path.Combine(Path.GetTempPath(), $"csp-token-{Guid.NewGuid():N}");
    private readonly StubHandler _handler = new();
    private readonly FakeTime _time = new();

    public CspTokenProviderTests()
    {
        File.WriteAllText(_assertionPath, "assertion-1\n");
        _handler.Responder = _ => Token("t");
    }

    public void Dispose()
    {
        if (File.Exists(_assertionPath))
        {
            File.Delete(_assertionPath);
        }
    }

    private CspTokenProvider CreateSut(
        string? cspUrl = "https://csp.example.com/",
        string? clientSecret = null,
        string environment = "Production",
        string? assertionPath = null) =>
        new(
            new HttpClient(_handler),
            new ServiceAuthOptions { AssertionPath = assertionPath ?? _assertionPath, ClientSecret = clientSecret },
            new CspKnowledgeOptions { BaseUrl = cspUrl },
            new FakeEnv(environment),
            _time,
            NullLogger<CspTokenProvider>.Instance);

    private static HttpResponseMessage Token(string token, int expiresIn = 3600) =>
        JsonResponse.Ok($$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":{{expiresIn}}}""");

    [Fact]
    public async Task Deployed_PostsTokenExchangeForm_WithNoAuthorizationHeader()
    {
        _handler.Responder = _ => Token("t1");

        var token = await CreateSut().GetTokenAsync(CancellationToken.None);

        Assert.Equal("t1", token);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://csp.example.com/api/v1/knowledge/token", request.Uri);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        Assert.Null(request.Authorization);
        Assert.Equal(
            [
                "grant_type=urn:ietf:params:oauth:grant-type:token-exchange",
                "subject_token=assertion-1",
                "subject_token_type=urn:ietf:params:oauth:token-type:jwt",
                "audience=csp"
            ],
            request.Form);
    }

    [Fact]
    public async Task Development_WithSecret_PostsClientCredentials()
    {
        await CreateSut(clientSecret: "s3cret", environment: "Development").GetTokenAsync(CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(["grant_type=client_credentials", "client_id=hub", "client_secret=s3cret"], request.Form);
    }

    [Fact]
    public async Task Production_IgnoresClientSecret_AndUsesTheAssertion()
    {
        await CreateSut(clientSecret: "s3cret", environment: "Production").GetTokenAsync(CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Contains("subject_token=assertion-1", request.Form);
        Assert.DoesNotContain(request.Form, f => f.Contains("client_secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Development_WithoutSecret_FallsBackToTheAssertion()
    {
        await CreateSut(environment: "Development").GetTokenAsync(CancellationToken.None);

        Assert.Contains("subject_token=assertion-1", Assert.Single(_handler.Requests).Form);
    }

    [Fact]
    public async Task RereadsTheAssertionFileOnEveryExchange()
    {
        var sut = CreateSut();
        await sut.GetTokenAsync(CancellationToken.None);

        File.WriteAllText(_assertionPath, "assertion-2");
        sut.Invalidate();
        await sut.GetTokenAsync(CancellationToken.None);

        var requests = _handler.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Contains("subject_token=assertion-1", requests[0].Form);
        Assert.Contains("subject_token=assertion-2", requests[1].Form);
    }

    [Fact]
    public async Task CachesUntilFiveMinutesBeforeExpiry()
    {
        var sut = CreateSut();
        await sut.GetTokenAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(3600 - 300 - 1));
        await sut.GetTokenAsync(CancellationToken.None);
        Assert.Single(_handler.Requests);

        _time.Advance(TimeSpan.FromSeconds(2));
        await sut.GetTokenAsync(CancellationToken.None);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task ShortLivedTokens_RefreshAtHalfLife()
    {
        _handler.Responder = _ => Token("t", expiresIn: 600);
        var sut = CreateSut();
        await sut.GetTokenAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(299));
        await sut.GetTokenAsync(CancellationToken.None);
        Assert.Single(_handler.Requests);

        _time.Advance(TimeSpan.FromSeconds(2));
        await sut.GetTokenAsync(CancellationToken.None);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task Invalidate_ForcesAFreshExchange()
    {
        var sut = CreateSut();
        await sut.GetTokenAsync(CancellationToken.None);

        sut.Invalidate();
        await sut.GetTokenAsync(CancellationToken.None);

        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentCallers_ShareASingleExchange()
    {
        var release = new TaskCompletionSource();
        _handler.Async = async (_, _) =>
        {
            await release.Task;
            return Token("shared");
        };
        var sut = CreateSut();

        var callers = Enumerable.Range(0, 10).Select(_ => sut.GetTokenAsync(CancellationToken.None)).ToArray();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        release.SetResult();
        var tokens = await Task.WhenAll(callers);

        Assert.All(tokens, t => Assert.Equal("shared", t));
        Assert.Single(_handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{"access_token":"","token_type":"Bearer","expires_in":60}""")]
    [InlineData(HttpStatusCode.OK, """{"access_token":"x","token_type":"MAC","expires_in":60}""")]
    [InlineData(HttpStatusCode.OK, """{"access_token":"x","token_type":"Bearer","expires_in":0}""")]
    public async Task BadResponses_ReturnNull_AndAreNotCached(HttpStatusCode status, string body)
    {
        _handler.Responder = _ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        var sut = CreateSut();

        Assert.Null(await sut.GetTokenAsync(CancellationToken.None));
        Assert.Null(await sut.GetTokenAsync(CancellationToken.None));
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task NetworkFailure_ReturnsNull()
    {
        _handler.Responder = _ => throw new HttpRequestException("refused");

        Assert.Null(await CreateSut().GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MissingOrEmptyAssertionFile_ReturnsNull_WithoutCallingCsp()
    {
        Assert.Null(await CreateSut(assertionPath: _assertionPath + ".missing").GetTokenAsync(CancellationToken.None));

        File.WriteAllText(_assertionPath, "  \n");
        Assert.Null(await CreateSut().GetTokenAsync(CancellationToken.None));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Failures_LogOneWarningPerMinute()
    {
        var logger = new CapturingLogger<CspTokenProvider>();
        var sut = new CspTokenProvider(
            new HttpClient(_handler), new ServiceAuthOptions { AssertionPath = _assertionPath + ".missing" },
            new CspKnowledgeOptions { BaseUrl = "https://csp.example.com" }, new FakeEnv("Production"), _time, logger);

        await sut.GetTokenAsync(CancellationToken.None);
        await sut.GetTokenAsync(CancellationToken.None);
        Assert.Single(logger.Entries);

        _time.Advance(TimeSpan.FromMinutes(2));
        await sut.GetTokenAsync(CancellationToken.None);
        Assert.Equal(2, logger.Entries.Count);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        _handler.Async = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Token("never");
        };

        var pending = CreateSut().GetTokenAsync(cts.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public void IsConfigured_NeedsBaseUrlAndACredential()
    {
        Assert.True(CreateSut().IsConfigured);
        Assert.False(CreateSut(cspUrl: null).IsConfigured);
        Assert.False(CreateSut(assertionPath: _assertionPath + ".missing").IsConfigured);
        Assert.True(CreateSut(assertionPath: _assertionPath + ".missing", clientSecret: "s", environment: "Development").IsConfigured);
        Assert.False(CreateSut(assertionPath: _assertionPath + ".missing", clientSecret: "s", environment: "Production").IsConfigured);
    }
}
