using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess.ClientMetadata;

// OpenIddict 7 follows draft-ietf-oauth-rfc7523bis for client assertions: the "typ" header must be
// client-authentication+jwt and the audience must be the issuer. A private_key_jwt client found through a client ID
// metadata document (ChatGPT) is not ours to change and may still sign the RFC 7523 shape: "typ" JWT (or none) and
// the token endpoint as audience. For those clients alone - a URL client_id - both forms are accepted; the signature is
// still checked against the client's own published keys, and every other check is OpenIddict's.
public static class AgentAccessClientAssertionCompatibility
{
    private static readonly string[] GenericTypes = [JsonWebTokenTypes.GenericJsonWebToken, "application/jwt"];

    private static bool AppliesTo(OpenIddictServerEvents.ValidateTokenContext context) =>
        context.ValidTokenTypes is { Count: 1 } types
        && types.Contains(TokenTypeIdentifiers.Private.ClientAssertion)
        && context.Request?.ClientId is { } clientId
        && clientId.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    // Before the token is validated: widen the accepted "typ" values and audiences.
    public sealed class AcceptRfc7523Shape : IOpenIddictServerHandler<OpenIddictServerEvents.ValidateTokenContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
            OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ValidateTokenContext>()
                .UseSingletonHandler<AcceptRfc7523Shape>()
                .SetOrder(OpenIddictServerHandlers.Protection.ValidateIdentityModelToken.Descriptor.Order - 500)
                .SetType(OpenIddictServerHandlerType.Custom)
                .Build();

        public ValueTask HandleAsync(OpenIddictServerEvents.ValidateTokenContext context)
        {
            if (!AppliesTo(context)) return ValueTask.CompletedTask;

            var parameters = context.TokenValidationParameters;
            parameters.ValidTypes = [.. parameters.ValidTypes ?? [], .. GenericTypes];

            if ((context.Options.Issuer ?? context.BaseUri) is { IsAbsoluteUri: true } issuer)
            {
                foreach (var endpoint in context.Options.TokenEndpointUris)
                {
                    var absolute = endpoint.IsAbsoluteUri
                        ? endpoint
                        : new Uri(new Uri(issuer.AbsoluteUri.TrimEnd('/') + "/"), endpoint.OriginalString.TrimStart('/'));
                    context.ValidAudiences.Add(absolute.AbsoluteUri);
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    // After the token is read: OpenIddict maps a generic JWT to an identity token, so put it back as a client assertion.
    public sealed class RestoreClientAssertionType : IOpenIddictServerHandler<OpenIddictServerEvents.ValidateTokenContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
            OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ValidateTokenContext>()
                .UseSingletonHandler<RestoreClientAssertionType>()
                .SetOrder(OpenIddictServerHandlers.Protection.ValidateIdentityModelToken.Descriptor.Order + 500)
                .SetType(OpenIddictServerHandlerType.Custom)
                .Build();

        public ValueTask HandleAsync(OpenIddictServerEvents.ValidateTokenContext context)
        {
            if (AppliesTo(context) && context.Principal is { } principal
                && principal.GetTokenType() == TokenTypeIdentifiers.IdentityToken)
            {
                principal.SetTokenType(TokenTypeIdentifiers.Private.ClientAssertion);
            }

            return ValueTask.CompletedTask;
        }
    }
}
