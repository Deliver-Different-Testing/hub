using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Hub.Features.AiAssistant;

/// <summary>Each Auto-mate message is a paid model call, so it is limited per signed-in user.</summary>
public static class AssistantRateLimiting
{
    public const string PolicyName = "assistant";
    public const int PermitsPerMinute = 20;

    public static void Add(RateLimiterOptions options) =>
        options.AddPolicy(PolicyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context.User),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

    public static string PartitionKey(ClaimsPrincipal user)
    {
        var id = user.FindFirst("UserID")?.Value ?? user.FindFirst(ClaimTypes.Name)?.Value;
        return string.IsNullOrWhiteSpace(id) ? "anonymous" : $"user:{id}";
    }
}
