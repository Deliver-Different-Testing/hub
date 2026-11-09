using Anthropic.Credentials;
using Anthropic.Exceptions;

namespace Hub.Features.AiAssistant;

/// <summary>Why a Claude call failed, as far as the user needs to react differently.</summary>
public enum LlmFailure
{
    /// <summary>Per-request problem (timeout, 5xx): worth a retry.</summary>
    Other,
    /// <summary>The configured model does not exist: every call fails until it is fixed.</summary>
    Config,
    /// <summary>The identity token is missing or Anthropic refused it: every call fails until it is fixed.</summary>
    NotConnected,
    /// <summary>429: the account is being throttled.</summary>
    RateLimited,
    /// <summary>400: the request itself was rejected, so retrying it unchanged will not help.</summary>
    BadRequest
}

public static class AnthropicErrors
{
    public static LlmFailure Classify(Exception ex) => ex switch
    {
        AnthropicUnauthorizedException or AnthropicForbiddenException or WorkloadIdentityException
            or FileNotFoundException or DirectoryNotFoundException => LlmFailure.NotConnected,
        AnthropicNotFoundException => LlmFailure.Config,
        AnthropicRateLimitException => LlmFailure.RateLimited,
        AnthropicBadRequestException => LlmFailure.BadRequest,
        _ => LlmFailure.Other
    };
}
