using System.Text.Json;
using Anthropic.Models.Beta.Messages;

namespace Hub.Features.AiAssistant.Tools;

public interface IAiTool
{
    BetaTool Definition { get; }
    Task<string> ExecuteAsync(JsonElement input, CancellationToken ct);
}

/// <summary>A tool backed by the CSP knowledge base: only offered when CSP is configured.</summary>
public interface IKnowledgeBaseTool : IAiTool;
