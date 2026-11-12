using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace Hub.Features.AgentAccess.Grants;

// The server-to-server key Integration Manager sends (X-Api-Key). It is the partner-directory key IM already holds,
// so there is no new secret to hand out. Compared in constant time.
public static class AgentAccessApiKey
{
    public static bool IsValid(string? apiKey) => ApiKeyCheck.Matches(apiKey, "PartnerDirectoryApiKey", "Agent access API");
}

internal static class ApiKeyCheck
{
    internal static bool Matches(string? apiKey, string envName, string logLabel)
    {
        var expected = Environment.GetEnvironmentVariable(envName) ?? string.Empty;
        if (!string.IsNullOrEmpty(expected) && apiKey is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes(expected)))
        {
            return true;
        }

        Log.Warning("{Label} request rejected: invalid or missing API key", logLabel);
        return false;
    }
}
