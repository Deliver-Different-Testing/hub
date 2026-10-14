using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Hub.Interfaces;

namespace Hub.Services;

/// <summary>
/// Reads the OAuth key parameters from SSM. The pod IAM policy allows ssm:GetParametersByPath
/// (not GetParameter), so this reads the whole prefix, decrypted, page by page.
/// </summary>
public sealed class SsmAgentAccessKeySource(IAmazonSimpleSystemsManagement ssm) : IAgentAccessKeySource
{
    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        string prefix, CancellationToken cancellationToken = default)
    {
        var path = prefix.TrimEnd('/');
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? nextToken = null;

        do
        {
            var response = await ssm.GetParametersByPathAsync(
                new GetParametersByPathRequest
                {
                    Path = path,
                    WithDecryption = true,
                    Recursive = false,
                    NextToken = nextToken
                },
                cancellationToken);

            foreach (var parameter in response.Parameters)
            {
                var leaf = parameter.Name[(parameter.Name.LastIndexOf('/') + 1)..];
                result[leaf] = parameter.Value;
            }

            nextToken = response.NextToken;
        } while (!string.IsNullOrEmpty(nextToken));

        return result;
    }
}
