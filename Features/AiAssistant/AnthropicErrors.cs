using Anthropic.Exceptions;

namespace Hub.Features.AiAssistant;

/// <summary>Why a Claude call failed, as far as the user needs to react differently.</summary>
public enum LlmFailure
{
    /// <summary>Per-request problem (timeout, 5xx): worth a retry.</summary>
    Other,
    /// <summary>The API key is missing, revoked or lacks access, or the model does not exist: every call fails until it is fixed.</summary>
    Config,
    /// <summary>429: the account is being throttled.</summary>
    RateLimited,
    /// <summary>400: the request itself was rejected, so retrying it unchanged will not help.</summary>
    BadRequest
}

public static class AnthropicErrors
{
    public static LlmFailure Classify(Exception ex) => ex switch
    {
        AnthropicUnauthorizedException or AnthropicForbiddenException or AnthropicNotFoundException => LlmFailure.Config,
        AnthropicRateLimitException => LlmFailure.RateLimited,
        AnthropicBadRequestException => LlmFailure.BadRequest,
        _ => LlmFailure.Other
    };
}
