using System.Diagnostics.CodeAnalysis;
using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
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
}
