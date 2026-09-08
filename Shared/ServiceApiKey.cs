using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace Hub.Shared;

/// <summary>
/// The service-to-service API key check.
/// <para>
/// Shared rather than private to one controller because several use it. Two copies of an
/// authentication check drift, and the copy that drifts is the one nobody is looking at.
/// </para>
/// <para>
/// There were two tiers until the Shopify front door stopped calling Hub. It had a key of its own -
/// <c>ShopifySetupApiKey</c>, a <c>SetupService</c> tier worth only the handful of calls it made -
/// because it answers a public Shopify App URL reachable by any store on the internet, and a
/// compromise there had to not become a compromise of every tenant's database. It checks a
/// merchant's credentials against master itself now and asks Hub nothing, so the endpoints that
/// tier opened are gone and the tier with them. <c>ShopifySetupApiKey</c> is no longer read: it can
/// come out of the deployment's variables.
/// </para>
/// </summary>
public static class ServiceApiKey
{
    public static bool IsValid(string? apiKey)
    {
        if (Matches(apiKey, Environment.GetEnvironmentVariable("PartnerDirectoryApiKey")))
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
