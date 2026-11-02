using System.Text.Json;
using Hub.Features.AiAssistant;
using Hub.Features.AiAssistant.Tools;
using NSubstitute;

namespace Hub.Tests.Features.AiAssistant.Tools;

public class KnowledgeBaseToolTests
{
    private static readonly Guid Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ICspKnowledgeClient _client = Substitute.For<ICspKnowledgeClient>();
    private readonly KbTurnSources _sources = new();
    private readonly AiAssistantOptions _options = new();

    private SearchKnowledgeBaseTool Search() => new(_client, _sources, _options);

    private GetArticleTool Article() => new(_client, _sources, _options);

    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Search_WrapsHitsAsUntrustedData_AndRecordsSources()
    {
        _client.SearchAsync("fuel", 5, TimeSpan.FromMilliseconds(5000), Arg.Any<CancellationToken>())
            .Returns(new CspKbSearchResult(CspKbStatus.Ok,
                [new CspKbHit(Id, "Fuel <b>surcharge</b>", "snippet", null, null, false, 1, "Opening paragraph.\nSecond.")]));

        var result = await Search().ExecuteAsync(Input("""{"query":" fuel "}"""), CancellationToken.None);

        Assert.StartsWith("<kb-result>", result);
        Assert.EndsWith("</kb-result>", result);
        Assert.Contains($"- [{Id}] Fuel  b surcharge /b : Opening paragraph.", result);
        Assert.DoesNotContain("Second.", result);
        Assert.Equal("Fuel <b>surcharge</b>", Assert.Single(_sources.Seen).Title);
        Assert.False(ToolOutput.IsError(result));
    }

    [Fact]
    public async Task Search_Unavailable_IsAnErrorTellingTheModelToAnswerFromItsOwnKnowledge()
    {
        _client.SearchAsync(null!, 0, TimeSpan.Zero, TestContext.Current.CancellationToken).ReturnsForAnyArgs(CspKbSearchResult.Unavailable);

        var result = await Search().ExecuteAsync(Input("""{"query":"x"}"""), CancellationToken.None);

        Assert.True(ToolOutput.IsError(result));
        Assert.Contains("own domain knowledge", result);
        Assert.Contains("couldn't be checked", result);
    }

    [Fact]
    public async Task Search_NoHits_SaysSo()
    {
        _client.SearchAsync(null!, 0, TimeSpan.Zero, TestContext.Current.CancellationToken).ReturnsForAnyArgs(new CspKbSearchResult(CspKbStatus.Ok, []));

        var result = await Search().ExecuteAsync(Input("""{"query":"x"}"""), CancellationToken.None);

        Assert.Equal("No knowledge base articles matched.", result);
        Assert.Empty(_sources.Seen);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"query":"  "}""")]
    [InlineData("""{"query":5}""")]
    public async Task Search_BadInput_IsAToolError_WithoutCallingCsp(string json)
    {
        var result = await Search().ExecuteAsync(Input(json), CancellationToken.None);

        Assert.True(ToolOutput.IsError(result));
        await _client.DidNotReceiveWithAnyArgs().SearchAsync(null!, 0, TimeSpan.Zero, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetArticle_ReturnsContentAndFaqs_AndRecordsTheSource()
    {
        _client.GetArticleAsync(Id, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new CspKbArticleResult(CspKbStatus.Ok,
                new CspKbArticle(Id, "How to switch", "Step 1.", [new CspKbFaq("Why?", "Because.")])));

        var result = await Article().ExecuteAsync(Input($$"""{"article_id":"{{Id}}"}"""), CancellationToken.None);

        Assert.Contains("How to switch", result);
        Assert.Contains("Step 1.", result);
        Assert.Contains("Q: Why?\nA: Because.", result);
        Assert.Equal(Id, Assert.Single(_sources.Seen).ArticleId);
    }

    [Fact]
    public async Task GetArticle_Unavailable_IsTheUnavailableError()
    {
        _client.GetArticleAsync(Guid.Empty, TimeSpan.Zero, TestContext.Current.CancellationToken).ReturnsForAnyArgs(CspKbArticleResult.Unavailable);

        var result = await Article().ExecuteAsync(Input($$"""{"article_id":"{{Id}}"}"""), CancellationToken.None);

        Assert.True(ToolOutput.IsError(result));
        Assert.Contains("couldn't be checked", result);
    }

    [Fact]
    public async Task GetArticle_NotFound_IsAToolError_WithNoSource()
    {
        _client.GetArticleAsync(Guid.Empty, TimeSpan.Zero, TestContext.Current.CancellationToken).ReturnsForAnyArgs(new CspKbArticleResult(CspKbStatus.NotFound, null));

        var result = await Article().ExecuteAsync(Input($$"""{"article_id":"{{Id}}"}"""), CancellationToken.None);

        Assert.True(ToolOutput.IsError(result));
        Assert.Contains("No knowledge base article has that id", result);
        Assert.Empty(_sources.Seen);
    }

    [Fact]
    public async Task GetArticle_InvalidGuid_IsAToolError_WithoutCallingCsp()
    {
        var result = await Article().ExecuteAsync(Input("""{"article_id":"nope"}"""), CancellationToken.None);

        Assert.True(ToolOutput.IsError(result));
        await _client.DidNotReceiveWithAnyArgs().GetArticleAsync(Guid.Empty, TimeSpan.Zero, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void KbText_StripsAngleBrackets()
    {
        Assert.Equal("a  b ", KbText.Clean("a <b>"));
    }
}
