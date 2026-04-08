using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Hub.Tests.Services;

[Collection("EnvironmentVariables")]
public class TenantLogoServiceTests : IDisposable
{
    private readonly IAmazonS3 _mockS3;
    private readonly MemoryCache _cache;
    private readonly string _originalBucket;

    public TenantLogoServiceTests()
    {
        _mockS3 = Substitute.For<IAmazonS3>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _originalBucket = Environment.GetEnvironmentVariable("S3BucketBulk") ?? string.Empty;
        Environment.SetEnvironmentVariable("S3BucketBulk", "test-bucket");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _cache.Dispose();
        Environment.SetEnvironmentVariable("S3BucketBulk", _originalBucket);
    }

    private Hub.Services.TenantLogoService CreateService() => new(_mockS3, _cache);

    [Fact]
    public async Task GetLogoUrlAsync_Cached_ReturnsCachedValue()
    {
        var service = CreateService();
        _cache.Set("tenant_logo_url_test-bucket", "https://cached-url.com", TimeSpan.FromMinutes(30));

        var result = await service.GetLogoUrlAsync();

        Assert.Equal("https://cached-url.com", result);
        await _mockS3.DidNotReceive().GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), CancellationToken.None);
    }

    [Fact]
    public async Task GetLogoUrlAsync_S3Exists_ReturnsPreSignedUrl()
    {
        _mockS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), CancellationToken.None)
            .Returns(new GetObjectMetadataResponse());
        _mockS3.GetPreSignedURLAsync(Arg.Any<GetPreSignedUrlRequest>())
            .Returns("https://s3.amazonaws.com/test-bucket/tenantLogo.png?signed=true");
        var service = CreateService();

        var result = await service.GetLogoUrlAsync();

        Assert.StartsWith("https://s3.amazonaws.com/", result);
    }

    [Fact]
    public async Task GetLogoUrlAsync_S3NotFound_ReturnsFallback()
    {
        _mockS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), CancellationToken.None).ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound });
        var service = CreateService();

        var result = await service.GetLogoUrlAsync();

        Assert.Equal("/images/DFRNT_HorizLogo_RGB.png", result);
    }

    [Fact]
    public async Task GetLogoUrlAsync_EmptyBucket_ReturnsFallback()
    {
        Environment.SetEnvironmentVariable("S3BucketBulk", "");
        var service = CreateService();

        var result = await service.GetLogoUrlAsync();

        Assert.Equal("/images/DFRNT_HorizLogo_RGB.png", result);
    }

    [Fact]
    public async Task GetLogoUrlAsync_S3Exception_ReturnsFallback()
    {
        _mockS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), CancellationToken.None).ThrowsAsync(new AmazonS3Exception("Server Error") { StatusCode = HttpStatusCode.InternalServerError });
        var service = CreateService();

        var result = await service.GetLogoUrlAsync();

        Assert.Equal("/images/DFRNT_HorizLogo_RGB.png", result);
    }

    [Fact]
    public async Task GetLogoUrlAsync_CachesResultAfterFirstCall()
    {
        _mockS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), CancellationToken.None)
            .Returns(new GetObjectMetadataResponse());
        _mockS3.GetPreSignedURLAsync(Arg.Any<GetPreSignedUrlRequest>())
            .Returns("https://s3.amazonaws.com/presigned");
        var service = CreateService();

        await service.GetLogoUrlAsync();
        await service.GetLogoUrlAsync();

        // GetObjectMetadataAsync should only be called once (second call hits cache)
        await _mockS3.Received().GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), CancellationToken.None);
    }

    // LogoExistsAsync tests
    [Fact]
    public async Task LogoExistsAsync_Exists_ReturnsTrue()
    {
        _mockS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), CancellationToken.None)
            .Returns(new GetObjectMetadataResponse());
        var service = CreateService();

        var result = await service.LogoExistsAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task LogoExistsAsync_NotFound_ReturnsFalse()
    {
        _mockS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), CancellationToken.None).ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound });
        var service = CreateService();

        var result = await service.LogoExistsAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task LogoExistsAsync_Forbidden_ReturnsFalse()
    {
        _mockS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), CancellationToken.None).ThrowsAsync(new AmazonS3Exception("Forbidden") { StatusCode = HttpStatusCode.Forbidden });
        var service = CreateService();

        var result = await service.LogoExistsAsync();

        Assert.False(result);
    }

    // ClearCache tests
    [Fact]
    public void ClearCache_RemovesEntry()
    {
        var service = CreateService();
        _cache.Set("tenant_logo_url_test-bucket", "cached-value");

        service.ClearCache();

        Assert.False(_cache.TryGetValue("tenant_logo_url_test-bucket", out _));
    }
}
