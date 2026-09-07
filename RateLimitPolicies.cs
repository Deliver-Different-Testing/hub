namespace Hub;

/// <summary>Named rate-limit policies, so a controller and Program.cs cannot drift on a string.</summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Shopify merchant sign-in.
    /// <para>
    /// The only real policy here. <c>"api"</c> and <c>"auth"</c> are registered as deliberate no-ops:
    /// they were attributed long before the limiter existed, and choosing numbers for the admin
    /// endpoints is a separate decision. See Program.cs.
    /// </para>
    /// <para>
    /// This one is a password endpoint, reachable from the public internet by proxy, so it is not
    /// optional.
    /// <para>
    /// Partitioned by <em>shop</em> rather than caller IP: every request arrives from the front
    /// door's egress address, so an IP partition would let one merchant's typos throttle every
    /// merchant on every tenant. A global cap sits behind it, so a spray across many shop domains is
    /// still bounded.
    /// </para>
    /// </summary>
    public const string ShopifyMerchantSignIn = "shopify-merchant-signin";
}
