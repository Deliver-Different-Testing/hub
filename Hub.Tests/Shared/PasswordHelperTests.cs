using FluentAssertions;
using Hub.Shared;

namespace Hub.Tests.Shared;

public class PasswordHelperTests
{
    [Fact]
    public void HashPassword_IsDeterministic()
    {
        var hash1 = PasswordHelper.HashPassword("password123", "12345");
        var hash2 = PasswordHelper.HashPassword("password123", "12345");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void HashPassword_DifferentPasswords_ProduceDifferentHashes()
    {
        var hash1 = PasswordHelper.HashPassword("password1", "12345");
        var hash2 = PasswordHelper.HashPassword("password2", "12345");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void HashPassword_DifferentSalts_ProduceDifferentHashes()
    {
        var hash1 = PasswordHelper.HashPassword("password", "11111");
        var hash2 = PasswordHelper.HashPassword("password", "22222");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void HashPassword_Returns128CharHexString()
    {
        var hash = PasswordHelper.HashPassword("password", "12345");

        hash.Should().HaveLength(128);
        hash.Should().MatchRegex("^[0-9A-F]+$");
    }

    [Fact]
    public void HashPasswordLegacy_IsDeterministic()
    {
#pragma warning disable CS0618
        var hash1 = PasswordHelper.HashPasswordLegacy("password123", "12345");
        var hash2 = PasswordHelper.HashPasswordLegacy("password123", "12345");
#pragma warning restore CS0618

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void HashPasswordLegacy_ProducesDifferentOutputThanHashPassword()
    {
#pragma warning disable CS0618
        var legacyHash = PasswordHelper.HashPasswordLegacy("password", "12345");
#pragma warning restore CS0618
        var modernHash = PasswordHelper.HashPassword("password", "12345");

        legacyHash.Should().NotBe(modernHash);
    }

    [Fact]
    public void HashPasswordLegacy_Returns128CharHexString()
    {
#pragma warning disable CS0618
        var hash = PasswordHelper.HashPasswordLegacy("password", "12345");
#pragma warning restore CS0618

        hash.Should().HaveLength(128);
        hash.Should().MatchRegex("^[0-9A-F]+$");
    }

    [Fact]
    public void SaltHashNewPassword_ReturnsSaltBetween10000And99999()
    {
        var result = PasswordHelper.SaltHashNewPassword("Test@1234");

        var saltValue = int.Parse(result.Salt);
        saltValue.Should().BeGreaterThanOrEqualTo(10000);
        saltValue.Should().BeLessThan(99999);
        result.Salt.Should().HaveLength(5);
    }

    [Fact]
    public void SaltHashNewPassword_HashMatchesHashPassword()
    {
        var result = PasswordHelper.SaltHashNewPassword("Test@1234");

        var expectedHash = PasswordHelper.HashPassword("Test@1234", result.Salt);
        result.Hashed.Should().Be(expectedHash);
    }

    [Fact]
    public void SaltHashNewPassword_ReturnsNonNullSaltAndHash()
    {
        var result = PasswordHelper.SaltHashNewPassword("password");

        result.Should().NotBeNull();
        result.Salt.Should().NotBeNullOrEmpty();
        result.Hashed.Should().NotBeNullOrEmpty();
    }
}
