using System.Text;
using System.Text.Json;
using Anthropic.Models.Beta.Messages;

namespace Hub.Features.AiAssistant.Tools;

public sealed class GetArticleTool(ICspKnowledgeClient client, KbTurnSources sources, AiAssistantOptions options) : IKnowledgeBaseTool
{
    private static readonly BetaTool Tool = new()
    {
        Name = "get_article",
        Description = "Read one knowledge base article in full, including its FAQs. Use it when the lead of an article in the knowledge-base block or in search results does not give the steps.",
        InputSchema = new InputSchema
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["article_id"] = ToolSchema.Prop("string", "The article GUID shown in square brackets")
            },
            Required = ["article_id"]
        }
    };

    public BetaTool Definition => Tool;

    public async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var args = new ToolArgs(input);
        var raw = args.String("article_id");

        if (args.Error is not null || !Guid.TryParse(raw, out var id))
        {
            return ToolOutput.Error(args.Error ?? "'article_id' is required and must be the GUID shown in square brackets.");
        }

        var result = await client.GetArticleAsync(id, TimeSpan.FromMilliseconds(options.KbToolTimeoutMs), ct);
        if (result.Status == CspKbStatus.Unavailable)
        {
            return ToolOutput.Error(KbText.UnavailableMessage);
        }

        if (result.Article is not { } article)
        {
            return ToolOutput.Error("No knowledge base article has that id.");
        }

        sources.Add(id, article.Title, isFaq: false);

        var sb = new StringBuilder("<kb-result>\n")
            .Append(KbText.Clean(article.Title)).Append('\n').Append(KbText.Article(article.Content));
        foreach (var faq in article.Faqs ?? [])
        {
            sb.Append("\n\nQ: ").Append(KbText.Clean(faq.Question)).Append("\nA: ").Append(KbText.Clean(faq.Answer));
        }

        return sb.Append("\n</kb-result>").ToString();
    }
}
