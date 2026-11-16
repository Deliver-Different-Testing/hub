using System.Diagnostics.CodeAnalysis;
using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.SimpleSystemsManagement;
using Hub.Features.AgentAccess.Keys;
using Serilog;

namespace Hub.Extensions;

[ExcludeFromCodeCoverage]
public static class AwsServiceExtensions
{
    public static IServiceCollection AddAwsServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAmazonS3>(_ =>
        {
            var awsOptions = configuration.GetAWSOptions();

            Log.Information("AWS Region from config: {Region}", awsOptions.Region?.SystemName ?? "null");

            var s3Config = new AmazonS3Config
            {
                RegionEndpoint = awsOptions.Region ?? RegionEndpoint.APSoutheast2
            };

            var chain = new CredentialProfileStoreChain();
            return chain.TryGetAWSCredentials("default", out var credentials) ? new AmazonS3Client(credentials, s3Config) :
                // Falls back to the SDK's default credential resolution chain
                // (env vars, default profile, container creds, EC2 instance metadata)
                new AmazonS3Client(s3Config);
        });

        return services;
    }

    /// <summary>
    /// SSM reader and creator for the OAuth keys (phase 1.3). Short timeout and one SDK retry: the bootstrap's own
    /// attempts, each with its own deadline, do the real retrying, so the two must not multiply into a long stall.
    /// The pod role allows ssm:GetParametersByPath and ssm:PutParameter.
    /// </summary>
    public static IAgentAccessKeySource CreateAgentAccessKeySource(IConfiguration configuration, string? kmsKeyId)
    {
        var awsOptions = configuration.GetAWSOptions();
        var config = new AmazonSimpleSystemsManagementConfig
        {
            RegionEndpoint = awsOptions.Region ?? RegionEndpoint.APSoutheast2,
            Timeout = TimeSpan.FromSeconds(5),
            RetryMode = RequestRetryMode.Standard,
            MaxErrorRetry = 1
        };

        return new SsmAgentAccessKeySource(new AmazonSimpleSystemsManagementClient(config), kmsKeyId);
    }
}
