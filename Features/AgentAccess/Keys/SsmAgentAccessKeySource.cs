using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

namespace Hub.Features.AgentAccess.Keys;

/// <summary>
/// Reads and creates the OAuth key parameters in SSM. The pod IAM policy allows ssm:GetParametersByPath
/// (not GetParameter), so this reads the whole prefix, decrypted, page by page, and creates with
/// ssm:PutParameter, never overwriting.
/// </summary>
/// <param name="kmsKeyId">KMS key for new SecureString parameters (env AgentAccessKmsKeyId). Unset = SSM's default key.</param>
public sealed class SsmAgentAccessKeySource(IAmazonSimpleSystemsManagement ssm, string? kmsKeyId = null) : IAgentAccessKeySource
{
    public async Task<IReadOnlyDictionary<string, AgentAccessKeyParameter>> ReadAsync(
        string prefix, CancellationToken cancellationToken = default)
    {
        var path = prefix.TrimEnd('/');
        var result = new Dictionary<string, AgentAccessKeyParameter>(StringComparer.Ordinal);
        string? nextToken = null;

        do
        {
            var response = await ssm.GetParametersByPathAsync(
                new GetParametersByPathRequest
                {
                    Path = path,
                    WithDecryption = true,
                    // Created keys sit one level down, under signing/ and encryption/.
                    Recursive = true,
                    NextToken = nextToken
                },
                cancellationToken);

            foreach (var parameter in response.Parameters)
            {
                var name = parameter.Name.StartsWith(path + "/", StringComparison.Ordinal)
                    ? parameter.Name[(path.Length + 1)..]
                    : parameter.Name[(parameter.Name.LastIndexOf('/') + 1)..];
                result[name] = new AgentAccessKeyParameter(parameter.Value, parameter.LastModifiedDate ?? DateTime.MaxValue);
            }

            nextToken = response.NextToken;
        } while (!string.IsNullOrEmpty(nextToken));

        return result;
    }

    public async Task CreateAsync(string prefix, string name, string value, CancellationToken cancellationToken = default)
    {
        await ssm.PutParameterAsync(
            new PutParameterRequest
            {
                Name = $"{prefix.TrimEnd('/')}/{name}",
                Value = value,
                Type = ParameterType.SecureString,
                Overwrite = false,
                KeyId = string.IsNullOrWhiteSpace(kmsKeyId) ? null : kmsKeyId.Trim()
            },
            cancellationToken);
    }
}

/// <param name="Value">The decrypted parameter value.</param>
/// <param name="LastModified">When SSM last wrote the parameter (UTC); the same on every pod that reads it.</param>
public sealed record AgentAccessKeyParameter(string Value, DateTime LastModified);

public interface IAgentAccessKeySource
{
    /// <summary>
    /// Reads every parameter under the prefix, keyed by its name below it (SigningKey, EncryptionKey,
    /// signing/&lt;kid&gt;, ...).
    /// </summary>
    Task<IReadOnlyDictionary<string, AgentAccessKeyParameter>> ReadAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a SecureString parameter at prefix/name. Never overwrites: throws when it already exists.
    /// </summary>
    Task CreateAsync(string prefix, string name, string value, CancellationToken cancellationToken = default);
}
