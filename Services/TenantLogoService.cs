using System;
using System.Threading.Tasks;
using Amazon.Runtime.Internal;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Hub.Services;

public interface ITenantLogoService
{
    Task<string> GetLogoUrlAsync();
    Task<bool> LogoExistsAsync();
    void ClearCache();
}

public class TenantLogoService : ITenantLogoService
{
    private readonly IAmazonS3 _s3Client;
    private readonly IMemoryCache _cache;
    private readonly string? _bucketName;
    private const string LogoKey = "tenantLogo.png";
    private readonly string? _fallbackLogoPath = "/images/deliverDifferentLogo.png";
    private const string LogoCacheKey = "tenant_logo_url";
    private const int CacheDurationMinutes = 30;

    public TenantLogoService(IAmazonS3 s3Client, IMemoryCache cache)
    {
        _s3Client = s3Client;
        _cache = cache;
        _bucketName = Environment.GetEnvironmentVariable("S3BucketBulk");

        if (string.IsNullOrEmpty(_bucketName))
        {
            Log.Warning("S3BucketBulk environment variable not set");
        }
    }

    public async Task<string?> GetLogoUrlAsync()
    {
        // Check cache first
        if (_cache.TryGetValue(LogoCacheKey, out string? cachedUrl))
        {
            Log.Debug("Retrieved tenant logo URL from cache: {LogoUrl}", cachedUrl);
            return cachedUrl;
        }

        string? logoUrl = _fallbackLogoPath;
        Log.Information("S3BucketBulk environment variable value: {BucketName}", _bucketName ?? "null");

        try
        {
            if (!string.IsNullOrEmpty(_bucketName))
            {
                if (await LogoExistsInS3Async())
                {
                    // Generate pre-signed URL for the logo (valid for 1 hour)
                    var request = new GetPreSignedUrlRequest
                    {
                        BucketName = _bucketName,
                        Key = LogoKey,
                        Verb = HttpVerb.GET,
                        Expires = DateTime.UtcNow.AddHours(1)
                    };

                    logoUrl = await _s3Client.GetPreSignedURLAsync(request);
                    Log.Information("Generated pre-signed URL for tenant logo from S3");
                }
                else
                {
                    Log.Information("Tenant logo not found in S3, using fallback: {FallbackPath}", logoUrl);
                }
            }
            else
            {
                Log.Information("S3BucketBulk not configured, using fallback: {FallbackPath}", logoUrl);
            }

            // Cache the result
            Log.Information("Caching logo URL: {LogoUrl}", logoUrl);
            _cache.Set(LogoCacheKey, logoUrl, TimeSpan.FromMinutes(CacheDurationMinutes));
            return logoUrl;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving tenant logo from S3, using fallback: {FallbackPath}", logoUrl);

            // Cache the fallback to avoid repeated failed S3 calls
            _cache.Set(LogoCacheKey, logoUrl, TimeSpan.FromMinutes(5));
            return logoUrl;
        }
    }

    public async Task<bool> LogoExistsAsync()
    {
        return await LogoExistsInS3Async();
    }

    public void ClearCache()
    {
        _cache.Remove(LogoCacheKey);
        Log.Information("Tenant logo cache cleared");
    }

    private async Task<bool> LogoExistsInS3Async()
    {
        try
        {
            if (string.IsNullOrEmpty(_bucketName))
            {
                Log.Warning("Cannot check S3 logo - bucket name is empty");
                return false;
            }

            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = LogoKey
            };

            await _s3Client.GetObjectMetadataAsync(request);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Log.Debug("Tenant logo not found in S3 bucket {BucketName}", _bucketName);
            return false;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            Log.Warning(ex, "Access denied to S3 bucket {BucketName} - check IAM permissions", _bucketName);
            return false;
        }
        catch (AmazonS3Exception ex)
        {
            Log.Error(ex, "S3 error checking logo in bucket {BucketName}: {ErrorCode} - {Message}",
                _bucketName, ex.ErrorCode, ex.Message);
            return false;
        }
        catch (Amazon.Runtime.Internal.HttpErrorResponseException ex)
        {
            Log.Error(ex, "HTTP error connecting to S3 for bucket {BucketName} - possible credential or network issue", _bucketName);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error checking if tenant logo exists in S3 bucket {BucketName}", _bucketName);
            return false;
        }
    }
}
