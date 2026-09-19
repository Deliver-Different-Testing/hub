using System.Security.Cryptography;
using Hub.Models;

namespace Hub.Shared;

public static class PasswordHelper
{
    /// <summary>
    /// call to create or check/verify a hashed password using the unique salt value
    /// </summary>
    /// <param name="password"></param>
    /// <param name="salt"></param>
    /// <returns></returns>
    /// <summary>
    /// Legacy hash kept only for verifying existing passwords during upgrade to HashPassword.
    /// Do not use for new passwords.
    /// </summary>
    public static string HashPasswordLegacy(string password, string salt)
    {
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            System.Text.Encoding.UTF8.GetBytes(salt + salt),
            1000,
            HashAlgorithmName.SHA1,
            64); // 64 bytes = 512 bits
        return Convert.ToHexString(hashBytes);
    }

    public static SaltHashed SaltHashNewPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salted = Convert.ToBase64String(saltBytes);
        var result = new SaltHashed
        {
            Salt = salted,
            Hashed = HashPassword(password, salted)
        };
        return result;
    }

    public static string HashPassword(string password, string salt)
    {
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            System.Text.Encoding.UTF8.GetBytes(salt + salt),
            10000,
            HashAlgorithmName.SHA256,
            64); // 64 bytes = 512 bits

        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Checks a password against a stored hash, choosing the algorithm the row was written with.
    /// <para>
    /// The one place that comparison happens. It used to live privately on
    /// <c>AccountController</c>, which was fine while the portal was the only caller; Shopify
    /// merchant sign-in is a second one, and it is reachable from the public internet by proxy.
    /// </para>
    /// <para>
    /// The compare is fixed-time. The ordinal <c>==</c> it replaces returns as soon as two
    /// characters differ, which leaks how much of a guessed hash was right - and a hex hash is
    /// exactly the shape an attacker can walk one character at a time.
    /// </para>
    /// <para>
    /// A row with no credential on it - an invited user who never set a password has both columns
    /// blank and <c>IsLegacyHash = false</c> - is refused explicitly rather than left to the
    /// coincidence that hashing against an empty salt does not produce an empty string.
    /// </para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Obsolete", "CS0618",
        Justification = "Legacy hash needed to verify users not yet upgraded")]
    public static bool Verify(string password, string salt, string storedHash, bool isLegacy)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(salt) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var hash = isLegacy
            ? HashPasswordLegacy(password, salt)
            : HashPassword(password, salt);

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(hash),
            System.Text.Encoding.UTF8.GetBytes(storedHash));
    }
}