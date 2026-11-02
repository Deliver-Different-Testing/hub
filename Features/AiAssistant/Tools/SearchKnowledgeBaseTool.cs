using System.Text;
using System.Text.Json;
using Anthropic.Models.Beta.Messages;

namespace Hub.Features.AiAssistant.Tools;

public sealed class SearchKnowledgeBaseTool(ICspKnowledgeClient client, KbTurnSources sources, AiAssistantOptions options) : IKnowledgeBaseTool
{
    private const int SearchLimit = 5;

    private static readonly BetaTool Tool = new()
    {
        Name = "search_knowledge_base",
        Description = "Search the DFRNT knowledge base of help articles and FAQs. Use it for how-to and product questions, or to look again with different words when the supplied knowledge-base block did not cover the question. Each result has its article id in square brackets; call get_article with that id to read a whole article.",
        InputSchema = new InputSchema
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["query"] = ToolSchema.Prop("string", "What to search for, in plain words")
            },
            Required = ["query"]
        }
    };

    public BetaTool Definition => Tool;

    public async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var args = new ToolArgs(input);
        var query = args.String("query");

        if (args.Error is not null || string.IsNullOrWhiteSpace(query))
        {
            return ToolOutput.Error(args.Error ?? "'query' is required and must be non-empty text.");
        }

        var result = await client.SearchAsync(query.Trim(), SearchLimit, TimeSpan.FromMilliseconds(options.KbToolTimeoutMs), ct);
        if (result.Status == CspKbStatus.Unavailable)
        {
            return ToolOutput.Error(KbText.UnavailableMessage);
        }

        if (result.Hits.Count == 0)
        {
            return "No knowledge base articles matched.";
        }

        var sb = new StringBuilder("<kb-result>");
        foreach (var hit in result.Hits)
        {
            sources.Add(hit.ArticleId, hit.Title, hit.IsFaq);
            sb.Append("\n- [").Append(hit.ArticleId).Append("] ").Append(KbText.Clean(hit.Title)).Append(": ")
                .Append(KbText.Lead(hit.Content ?? hit.Snippet));
        }

        return sb.Append("\n</kb-result>").ToString();
    }
}
