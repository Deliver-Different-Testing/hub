using System.Net;
using Hub.Features.AiAssistant;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Hub.Tests.Features.AiAssistant;

public class CspKnowledgeClientTests
{
    private static readonly Guid ArticleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    private readonly StubHandler _handler = new();
    private readonly FakeTime _time = new();
    private readonly IServiceTokenProvider _tokens = Substitute.For<IServiceTokenProvider>();
    private readonly CspKnowledgeCircuit _circuit;

    public CspKnowledgeClientTests()
    {
        _circuit = new CspKnowledgeCircuit(_time, NullLogger<CspKnowledgeCircuit>.Instance);
        _tokens.IsConfigured.Returns(true);
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("tok-1");
        _handler.Responder = _ => JsonResponse.Ok("[]");
    }

    private CspKnowledgeClient CreateSut(string? baseUrl = "https://csp.example.com/") =>
        new(new HttpClient(_handler), new CspKnowledgeOptions { BaseUrl = baseUrl }, _tokens, _circuit);

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    private const string HitJson = """
        [{"articleId":"11111111-1111-1111-1111-111111111111","title":"Switch organisation","snippet":"snip",
          "layer":"l","categoryName":"c","isFaq":false,"score":0.9,"content":"Use the switcher."}]
        """;

    [Fact]
    public async Task Search_SendsBearerAndQuery_AndParsesHits()
    {
        _handler.Responder = _ => JsonResponse.Ok(HitJson);

        var result = await CreateSut().SearchAsync("switch org & more", 3, Budget, CancellationToken.None);

        Assert.Equal(CspKbStatus.Ok, result.Status);
        var hit = Assert.Single(result.Hits);
        Assert.Equal("Switch organisation", hit.Title);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://csp.example.com/api/v1/knowledge/search?q=switch%20org%20%26%20more&limit=3", request.Uri);
        Assert.Equal("Bearer tok-1", request.Authorization);
    }

    [Fact]
    public async Task GetArticle_UsesTheArticleRoute()
    {
        _handler.Responder = _ => JsonResponse.Ok($$"""{"articleId":"{{ArticleId}}","title":"T","content":"C","faqs":[{"question":"Q","answer":"A"}]}""");

        var result = await CreateSut().GetArticleAsync(ArticleId, Budget, CancellationToken.None);

        Assert.Equal(CspKbStatus.Ok, result.Status);
        Assert.Equal("Q", Assert.Single(result.Article!.Faqs!).Question);
        Assert.Equal($"https://csp.example.com/api/v1/knowledge/articles/{ArticleId}", Assert.Single(_handler.Requests).Uri);
    }

    [Fact]
    public async Task Unauthorized_InvalidatesRefreshesAndRetriesOnce()
    {
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("old", "new");
        var calls = 0;
        _handler.Responder = _ => ++calls == 1 ? Status(HttpStatusCode.Unauthorized) : JsonResponse.Ok(HitJson);

        var result = await CreateSut().SearchAsync("q", 3, Budget, CancellationToken.None);

        Assert.Equal(CspKbStatus.Ok, result.Status);
        _tokens.Received(1).Invalidate();
        Assert.Equal(["Bearer old", "Bearer new"], _handler.Requests.Select(r => r.Authorization).ToArray());
        Assert.False(_circuit.IsOpen);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task SecondRefusal_IsPermanent_AndOpensTheCircuit(HttpStatusCode code)
    {
        _handler.Responder = _ => Status(code);
        var sut = CreateSut();

        var result = await sut.SearchAsync("q", 3, Budget, CancellationToken.None);

        Assert.Equal(CspKbStatus.Unavailable, result.Status);
        Assert.True(_circuit.IsOpen);
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.True(_circuit.IsOpen);
    }

    [Fact]
    public async Task Forbidden_IsNotRetried()
    {
        _handler.Responder = _ => Status(HttpStatusCode.Forbidden);

        await CreateSut().SearchAsync("q", 3, Budget, CancellationToken.None);

        Assert.Single(_handler.Requests);
        _tokens.DidNotReceive().Invalidate();
    }

    [Fact]
    public async Task SearchNotFound_IsPermanentUnavailable()
    {
        _handler.Responder = _ => Status(HttpStatusCode.NotFound);

        var result = await CreateSut().SearchAsync("q", 3, Budget, CancellationToken.None);

        Assert.Equal(CspKbStatus.Unavailable, result.Status);
        Assert.True(_circuit.IsOpen);
    }

    [Fact]
    public async Task ArticleNotFound_IsNotFound_AndLeavesTheCircuitClosed()
    {
        _handler.Responder = _ => Status(HttpStatusCode.NotFound);

        var result = await CreateSut().GetArticleAsync(ArticleId, Budget, CancellationToken.None);

        Assert.Equal(CspKbStatus.NotFound, result.Status);
        Assert.Null(result.Article);
        Assert.False(_circuit.IsOpen);
    }

    [Fact]
    public async Task OpenCircuit_MakesNoHttpCall()
    {
        _handler.Responder = _ => Status(HttpStatusCode.NotFound);
        var sut = CreateSut();
        await sut.SearchAsync("q", 3, Budget, CancellationToken.None);
        var before = _handler.Requests.Count;

        var search = await sut.SearchAsync("q", 3, Budget, CancellationToken.None);
        var article = await sut.GetArticleAsync(ArticleId, Budget, CancellationToken.None);

        Assert.Equal(CspKbStatus.Unavailable, search.Status);
        Assert.Equal(CspKbStatus.Unavailable, article.Status);
        Assert.Equal(before, _handler.Requests.Count);
    }

    [Fact]
    public async Task ThreeServerErrors_OpenTheCircuit()
    {
        _handler.Responder = _ => Status(HttpStatusCode.BadGateway);
        var sut = CreateSut();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(CspKbStatus.Unavailable, (await sut.SearchAsync("q", 3, Budget, CancellationToken.None)).Status);
        }

        Assert.True(_circuit.IsOpen);
        await sut.SearchAsync("q", 3, Budget, CancellationToken.None);
        Assert.Equal(3, _handler.Requests.Count);
    }

    [Fact]
    public async Task NetworkFailure_IsUnavailable_NotAnException()
    {
        _handler.Responder = _ => throw new HttpRequestException("refused");

        var result = await CreateSut().SearchAsync("q", 3, Budget, CancellationToken.None);

        Assert.Equal(CspKbStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task NoServiceToken_IsUnavailable_WithoutAnHttpCall()
    {
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await CreateSut().SearchAsync("q", 3, Budget, CancellationToken.None);

        Assert.Equal(CspKbStatus.Unavailable, result.Status);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task NotConfigured_IsUnavailable_WithoutAnHttpCall()
    {
        var result = await CreateSut(baseUrl: null).SearchAsync("q", 3, Budget, CancellationToken.None);

        Assert.Equal(CspKbStatus.Unavailable, result.Status);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task SlowCsp_IsCutAtTheBudget_AndOurTimeoutIsSwallowed()
    {
        _handler.Async = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return JsonResponse.Ok("[]");
        };

        var result = await CreateSut().SearchAsync("q", 3, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.Equal(CspKbStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        _handler.Async = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return JsonResponse.Ok("[]");
        };

        var pending = CreateSut().SearchAsync("q", 3, TimeSpan.FromSeconds(30), cts.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}
