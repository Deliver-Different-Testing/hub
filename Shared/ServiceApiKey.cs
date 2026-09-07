using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace Hub.Shared;

/// <summary>
/// The service-to-service API key check, and the two tiers it recognises.
/// <para>
/// Master holds every tenant's database credentials. The Deliver DFRNT front door is a public
/// Shopify App URL, reachable by any store on the internet including ones that never installed, so
/// it gets a key of its own worth only the handful of calls it makes - and a compromise there does
/// not become a compromise of every tenant's database.
/// </para>
/// <para>
/// Shared rather than private to one controller because there are two controllers now. Two copies of
/// an authentication check drift, and the copy that drifts is the one nobody is looking at.
/// </para>
/// </summary>
public static class ServiceApiKey
{
    public enum Caller
    {
        /// <summary>Staff tooling and the per-tenant Integration Manager deployments.</summary>
        Trusted,

        /// <summary>Those, and the Shopify front door.</summary>
        SetupService
    }

    public static bool IsValid(string? apiKey, Caller allowed = Caller.Trusted)
    {
        if (Matches(apiKey, Environment.GetEnvironmentVariable("PartnerDirectoryApiKey")))
        {
            return true;
        }

        if (allowed == Caller.SetupService &&
            Matches(apiKey, Environment.GetEnvironmentVariable("ShopifySetupApiKey")))
        {
            return true;
        }

        Log.Warning("Service request rejected: invalid or missing API key");
        return false;
    }

    /// <summary>
    /// An unset expected key closes the door rather than opening it to a caller sending nothing.
    /// The comparison is fixed-time because the caller may be anyone: an ordinal compare leaks how
    /// much of a guess was right, one character at a time.
    /// </summary>
    public static bool Matches(string? presented, string? expected)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));
    }
}
