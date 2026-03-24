using Hub.Shared;

namespace Hub.Tests.Shared;

public class PasswordHelperTests
{
    [Fact]
    public void HashPassword_IsDeterministic()
    {
        var hash1 = PasswordHelper.HashPassword("password123", "12345");
        var hash2 = PasswordHelper.HashPassword("password123", "12345");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashPassword_DifferentPasswords_ProduceDifferentHashes()
    {
        var hash1 = PasswordHelper.HashPassword("password1", "12345");
        var hash2 = PasswordHelper.HashPassword("password2", "12345");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashPassword_DifferentSalts_ProduceDifferentHashes()
    {
        var hash1 = PasswordHelper.HashPassword("password", "11111");
        var hash2 = PasswordHelper.HashPassword("password", "22222");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashPassword_Returns128CharHexString()
    {
        var hash = PasswordHelper.HashPassword("password", "12345");

        Assert.Equal(128, hash.Length);
        Assert.Matches("^[0-9A-F]+$", hash);
    }

    [Fact]
    public void HashPasswordLegacy_IsDeterministic()
    {
#pragma warning disable CS0618
        var hash1 = PasswordHelper.HashPasswordLegacy("password123", "12345");
        var hash2 = PasswordHelper.HashPasswordLegacy("password123", "12345");
#pragma warning restore CS0618

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashPasswordLegacy_ProducesDifferentOutputThanHashPassword()
    {
#pragma warning disable CS0618
        var legacyHash = PasswordHelper.HashPasswordLegacy("password", "12345");
#pragma warning restore CS0618
        var modernHash = PasswordHelper.HashPassword("password", "12345");

        Assert.NotEqual(legacyHash, modernHash);
    }

    [Fact]
    public void HashPasswordLegacy_Returns128CharHexString()
    {
#pragma warning disable CS0618
        var hash = PasswordHelper.HashPasswordLegacy("password", "12345");
#pragma warning restore CS0618

        Assert.Equal(128, hash.Length);
        Assert.Matches("^[0-9A-F]+$", hash);
    }

    [Fact]
    public void SaltHashNewPassword_ReturnsBase64Salt()
    {
        var result = PasswordHelper.SaltHashNewPassword("Test@1234");

        Assert.False(string.IsNullOrEmpty(result.Salt));
        var bytes = Convert.FromBase64String(result.Salt);
        Assert.Equal(16, bytes.Count());
    }

    [Fact]
    public void SaltHashNewPassword_HashMatchesHashPassword()
    {
        var result = PasswordHelper.SaltHashNewPassword("Test@1234");

        var expectedHash = PasswordHelper.HashPassword("Test@1234", result.Salt);
        Assert.Equal(expectedHash, result.Hashed);
    }

    [Fact]
    public void SaltHashNewPassword_ReturnsNonNullSaltAndHash()
    {
        var result = PasswordHelper.SaltHashNewPassword("password");

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Salt));
        Assert.False(string.IsNullOrEmpty(result.Hashed));
    }
}
