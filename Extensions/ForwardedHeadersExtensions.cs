using Microsoft.AspNetCore.HttpOverrides;

namespace Hub.Extensions;

public static class ForwardedHeadersExtensions
{
    // TLS ends at the nginx ingress, so the pod sees http. OpenIddict refuses http and builds its endpoint URLs from
    // the request scheme, so take the proto the ingress forwards. The ingress addresses vary across the cluster.
    public static IApplicationBuilder UseIngressForwardedHeaders(this IApplicationBuilder app)
    {
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        return app.UseForwardedHeaders(options);
    }
}
