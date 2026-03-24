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
}