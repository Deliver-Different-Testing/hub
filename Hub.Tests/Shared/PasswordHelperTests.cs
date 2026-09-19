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
        Assert.Equal(16, bytes.Length);
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

    // ------------------------------------------------------------------ Verify
    //
    // The one place a password is checked. It lives here rather than on a controller because there
    // are two callers now - the portal sign-in and the Shopify merchant sign-in - and the second is
    // reachable from the public internet by proxy. Two copies of a password comparison is one too
    // many, and the copy that existed was not constant-time.

    [Fact]
    public void Verify_AcceptsTheRightPassword()
    {
        var stored = PasswordHelper.HashPassword("Test@1234", "12345");

        Assert.True(PasswordHelper.Verify("Test@1234", "12345", stored, isLegacy: false));
    }

    [Fact]
    public void Verify_RejectsTheWrongPassword()
    {
        var stored = PasswordHelper.HashPassword("Test@1234", "12345");

        Assert.False(PasswordHelper.Verify("Test@12345", "12345", stored, isLegacy: false));
    }

    [Fact]
    public void Verify_WithALegacyRow_AcceptsTheRightPassword()
    {
        var stored = PasswordHelper.HashPasswordLegacy("Test@1234", "12345");

        Assert.True(PasswordHelper.Verify("Test@1234", "12345", stored, isLegacy: true));
    }

    /// <summary>
    /// The flag decides which algorithm runs, so getting it the wrong way round must fail closed
    /// rather than compare a SHA256 hash against a SHA1 one and land anywhere by chance.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verify_WithTheFlagTheWrongWayRound_Refuses(bool storedIsLegacy)
    {
        var stored = storedIsLegacy
            ? PasswordHelper.HashPasswordLegacy("Test@1234", "12345")
            : PasswordHelper.HashPassword("Test@1234", "12345");

        Assert.False(PasswordHelper.Verify("Test@1234", "12345", stored, isLegacy: !storedIsLegacy));
    }

    /// <summary>
    /// An invited user who has never set a password has both columns blank and
    /// <c>IsLegacyHash = false</c>. Hashing anything against an empty salt happens not to produce an
    /// empty string, so today that row is unreachable by accident rather than by decision. Decide it.
    /// </summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("", "12345")]
    [InlineData("SOMEHASH", "")]
    [InlineData(null, "12345")]
    [InlineData("SOMEHASH", null)]
    public void Verify_WithNoCredentialOnTheRow_Refuses(string? storedHash, string? salt)
    {
        Assert.False(PasswordHelper.Verify("Test@1234", salt!, storedHash!, isLegacy: false));
    }

    [Fact]
    public void Verify_WithAnEmptyPassword_Refuses()
    {
        var stored = PasswordHelper.HashPassword("x", "12345");

        Assert.False(PasswordHelper.Verify("", "12345", stored, isLegacy: false));
    }
}
