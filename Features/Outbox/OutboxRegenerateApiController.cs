using Hub.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

namespace Hub.Features.Outbox;

public sealed record OutboxRegeneratedMessage(string? Subject, string Body, bool IsHtml);

public sealed record OutboxRegenerationRefusal(string Reason);

// Called by this tenant's smppservice release worker (in-cluster) while it is sending a held password reset or
// Hub invite that a person released from the Outbox. Its own key (OutboxInternalApiKey), not the partner key:
// the answer contains a working reset link.
//   200 rebuilt message | 404 no such claimed row | 410 cannot be rebuilt (worker fails the row)
//   503 Hub cannot resolve its tenant (worker retries) | 401 bad key
[AllowAnonymous]
[EnableRateLimiting("api")]
public class OutboxRegenerateApiController(IOutboxResetRegenerator regenerator) : Controller
{
    [HttpPost("~/api/internal/outbox/{id:long}/regenerate")]
    public async Task<IActionResult> Regenerate(
        long id,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken cancellationToken)
    {
        if (!ServiceApiKey.Matches(apiKey, Environment.GetEnvironmentVariable("OutboxInternalApiKey")))
        {
            Log.Warning("Outbox regenerate request rejected: invalid or missing API key");
            return Unauthorized();
        }

        if (id <= 0) return BadRequest();

        var result = await regenerator.RegenerateAsync(id, cancellationToken);
        return result.Status switch
        {
            OutboxRegenerationStatus.Rebuilt => Ok(new OutboxRegeneratedMessage(result.Subject, result.Body!, result.IsHtml)),
            OutboxRegenerationStatus.NotFound => NotFound(),
            OutboxRegenerationStatus.Gone => StatusCode(StatusCodes.Status410Gone, new OutboxRegenerationRefusal(result.Reason!)),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new OutboxRegenerationRefusal(result.Reason ?? "Unavailable")),
        };
    }
}
