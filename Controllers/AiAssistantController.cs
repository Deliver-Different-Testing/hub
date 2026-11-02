using System.Text.Json;
using System.Text.Json.Serialization;
using Hub.Controllers.Api;
using Hub.Features.AiAssistant;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hub.Controllers;

/// <summary>Auto-mate chat. Derives from the SPA base so the POST needs the antiforgery header.</summary>
[Route("api/assistant")]
[Authorize]
[EnableRateLimiting(AssistantRateLimiting.PolicyName)]
public sealed class AiAssistantController(IAiAssistantService assistant, AiAssistantOptions options) : SpaApiControllerBase
{
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [HttpGet("status")]
    public IActionResult Status() => Ok(new { enabled = options.Enabled });

    [HttpPost("messages")]
    public async Task Messages([FromBody] AiChatRequest request, CancellationToken ct)
    {
        if (!options.Enabled)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await Response.WriteAsJsonAsync(new ApiError("Auto-mate isn't set up for this account."), ct);
            return;
        }

        try
        {
            await using var events = assistant.ChatStreamAsync(request, ct).GetAsyncEnumerator(ct);
            var hasFirst = await events.MoveNextAsync();

            // Nothing has been sent yet, so a failure before any output can still be a real HTTP error.
            if (hasFirst && events.Current is { Type: "error", HttpStatus: { } status } failure)
            {
                Response.StatusCode = status;
                await Response.WriteAsJsonAsync(new ApiError(failure.Error ?? "Auto-mate is unavailable right now. Try again later."), ct);
                return;
            }

            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            for (var more = hasFirst; more; more = await events.MoveNextAsync())
            {
                var json = JsonSerializer.Serialize(events.Current, EventJson);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The browser closed the stream mid-answer.
        }
    }
}
