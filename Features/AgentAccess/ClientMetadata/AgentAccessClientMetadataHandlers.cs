using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess.ClientMetadata;

// Phase 1.8: when an authorization request names an https URL as client_id, make sure the application row exists
// (fetching the client ID metadata document) before OpenIddict looks the client up. Authorization requests only:
// the token endpoint never triggers an outbound fetch.
public sealed class AgentAccessClientMetadataHandler(IClientMetadataService metadata)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ValidateAuthorizationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ValidateAuthorizationRequestContext>()
            .UseScopedHandler<AgentAccessClientMetadataHandler>()
            .SetOrder(OpenIddictServerHandlers.Authentication.ValidateClientIdParameter.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(OpenIddictServerEvents.ValidateAuthorizationRequestContext context)
    {
        if (context.Request.ClientId is not { } clientId || !clientId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var outcome = await metadata.EnsureAsync(clientId, context.Request.RedirectUri, context.CancellationToken);
        if (!outcome.Ready)
        {
            context.Reject(Errors.InvalidClient, "The client could not be verified.");
        }
    }
}

// Tells clients, in the discovery document, that a URL may be used as client_id.
public sealed class AgentAccessClientMetadataDiscoveryHandler(ClientMetadataOptions options)
    : IOpenIddictServerHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.HandleConfigurationRequestContext>()
            .UseSingletonHandler<AgentAccessClientMetadataDiscoveryHandler>()
            .SetOrder(OpenIddictServerHandlers.Discovery.AttachScopes.Descriptor.Order + 100)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(OpenIddictServerEvents.HandleConfigurationRequestContext context)
    {
        if (options.Enabled)
        {
            context.Metadata["client_id_metadata_document_supported"] = true;
        }

        return ValueTask.CompletedTask;
    }
}
