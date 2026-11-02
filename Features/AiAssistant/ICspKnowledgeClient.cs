namespace Hub.Features.AiAssistant;

public interface ICspKnowledgeClient
{
    bool IsConfigured { get; }
    Task<CspKbSearchResult> SearchAsync(string query, int limit, TimeSpan timeout, CancellationToken ct);
    Task<CspKbArticleResult> GetArticleAsync(Guid id, TimeSpan timeout, CancellationToken ct);
}

public enum CspKbStatus
{
    Ok,
    NotFound,
    Unavailable
}

public sealed record CspKbSearchResult(CspKbStatus Status, IReadOnlyList<CspKbHit> Hits)
{
    public static CspKbSearchResult Unavailable { get; } = new(CspKbStatus.Unavailable, []);
}

public sealed record CspKbArticleResult(CspKbStatus Status, CspKbArticle? Article)
{
    public static CspKbArticleResult Unavailable { get; } = new(CspKbStatus.Unavailable, null);
}

public sealed record CspKbHit(
    Guid ArticleId,
    string Title,
    string? Snippet,
    string? Layer,
    string? CategoryName,
    bool IsFaq,
    double Score,
    string? Content);

public sealed record CspKbFaq(string Question, string Answer);

public sealed record CspKbArticle(Guid ArticleId, string Title, string? Content, IReadOnlyList<CspKbFaq>? Faqs);
