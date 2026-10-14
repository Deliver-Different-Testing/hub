using System.Diagnostics.CodeAnalysis;
using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.SimpleSystemsManagement;
using Hub.Interfaces;
using Hub.Services;
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
    /// SSM reader for the OAuth keys (phase 1.3). Short timeout and one retry so a slow SSM cannot hold up
    /// startup; the pod role allows ssm:GetParametersByPath only.
    /// </summary>
    public static IAgentAccessKeySource CreateAgentAccessKeySource(IConfiguration configuration)
    {
        var awsOptions = configuration.GetAWSOptions();
        var config = new AmazonSimpleSystemsManagementConfig
        {
            RegionEndpoint = awsOptions.Region ?? RegionEndpoint.APSoutheast2,
            Timeout = TimeSpan.FromSeconds(5),
            MaxErrorRetry = 1
        };

        return new SsmAgentAccessKeySource(new AmazonSimpleSystemsManagementClient(config));
    }
}
