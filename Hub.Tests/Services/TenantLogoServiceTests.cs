using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace Hub.Tests.Services;

[Collection("EnvironmentVariables")]
public class TenantLogoServiceTests : IDisposable
{
    private readonly Mock<IAmazonS3> _mockS3;
    private readonly IMemoryCache _cache;
    private readonly string _originalBucket;

    public TenantLogoServiceTests()
    {
        _mockS3 = new Mock<IAmazonS3>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _originalBucket = Environment.GetEnvironmentVariable("S3BucketBulk") ?? string.Empty;
        Environment.SetEnvironmentVariable("S3BucketBulk", "test-bucket");
    }

    public void Dispose()
    {
        _cache.Dispose();
        Environment.SetEnvironmentVariable("S3BucketBulk", _originalBucket);
    }

    private Hub.Services.TenantLogoService CreateService() => new(_mockS3.Object, _cache);

    [Fact]
    public async Task GetLogoUrlAsync_Cached_ReturnsCachedValue()
    {
        var service = CreateService();
        _cache.Set("tenant_logo_url_test-bucket", "https://cached-url.com", TimeSpan.FromMinutes(30));

        var result = await service.GetLogoUrlAsync();

        result.Should().Be("https://cached-url.com");
        _mockS3.Verify(s => s.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None), Times.Never);
    }

    [Fact]
    public async Task GetLogoUrlAsync_S3Exists_ReturnsPreSignedUrl()
    {
        _mockS3
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
            .ReturnsAsync(new GetObjectMetadataResponse());
        _mockS3
            .Setup(s => s.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .ReturnsAsync("https://s3.amazonaws.com/test-bucket/tenantLogo.png?signed=true");
        var service = CreateService();

        var result = await service.GetLogoUrlAsync();

        result.Should().StartWith("https://s3.amazonaws.com/");
    }

    [Fact]
    public async Task GetLogoUrlAsync_S3NotFound_ReturnsFallback()
    {
        _mockS3
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
            .ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound });
        var service = CreateService();

        var result = await service.GetLogoUrlAsync();

        result.Should().Be("/images/DFRNT_HorizLogo_RGB.png");
    }

    [Fact]
    public async Task GetLogoUrlAsync_EmptyBucket_ReturnsFallback()
    {
        Environment.SetEnvironmentVariable("S3BucketBulk", "");
        var service = CreateService();

        var result = await service.GetLogoUrlAsync();

        result.Should().Be("/images/DFRNT_HorizLogo_RGB.png");
    }

    [Fact]
    public async Task GetLogoUrlAsync_S3Exception_ReturnsFallback()
    {
        _mockS3
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
            .ThrowsAsync(new AmazonS3Exception("Server Error") { StatusCode = HttpStatusCode.InternalServerError });
        var service = CreateService();

        var result = await service.GetLogoUrlAsync();

        result.Should().Be("/images/DFRNT_HorizLogo_RGB.png");
    }

    [Fact]
    public async Task GetLogoUrlAsync_CachesResultAfterFirstCall()
    {
        _mockS3
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
            .ReturnsAsync(new GetObjectMetadataResponse());
        _mockS3
            .Setup(s => s.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .ReturnsAsync("https://s3.amazonaws.com/presigned");
        var service = CreateService();

        await service.GetLogoUrlAsync();
        await service.GetLogoUrlAsync();

        // GetObjectMetadataAsync should only be called once (second call hits cache)
        _mockS3.Verify(s => s.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None), Times.Once);
    }

    // LogoExistsAsync tests
    [Fact]
    public async Task LogoExistsAsync_Exists_ReturnsTrue()
    {
        _mockS3
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
            .ReturnsAsync(new GetObjectMetadataResponse());
        var service = CreateService();

        var result = await service.LogoExistsAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task LogoExistsAsync_NotFound_ReturnsFalse()
    {
        _mockS3
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
            .ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound });
        var service = CreateService();

        var result = await service.LogoExistsAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task LogoExistsAsync_Forbidden_ReturnsFalse()
    {
        _mockS3
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
            .ThrowsAsync(new AmazonS3Exception("Forbidden") { StatusCode = HttpStatusCode.Forbidden });
        var service = CreateService();

        var result = await service.LogoExistsAsync();

        result.Should().BeFalse();
    }

    // ClearCache tests
    [Fact]
    public void ClearCache_RemovesEntry()
    {
        var service = CreateService();
        _cache.Set("tenant_logo_url_test-bucket", "cached-value");

        service.ClearCache();

        _cache.TryGetValue("tenant_logo_url_test-bucket", out _).Should().BeFalse();
    }
}
