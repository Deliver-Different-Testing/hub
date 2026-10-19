using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace Hub.Features.AgentAccess.Grants;

// The server-to-server key Integration Manager sends (X-Api-Key). It is the partner-directory key IM already holds,
// so there is no new secret to hand out. Compared in constant time.
public static class AgentAccessApiKey
{
    public static bool IsValid(string? apiKey)
    {
        var expected = Environment.GetEnvironmentVariable("PartnerDirectoryApiKey") ?? string.Empty;
        if (!string.IsNullOrEmpty(expected) && apiKey is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes(expected)))
        {
            return true;
        }

        Log.Warning("Agent access API request rejected: invalid or missing API key");
        return false;
    }
}
