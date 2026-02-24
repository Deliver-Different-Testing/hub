using System;
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
    [Obsolete("Obsolete")]
    public static string HashPasswordLegacy(string password, string salt)
    {
        var k2 = new Rfc2898DeriveBytes(password, System.Text.Encoding.UTF8.GetBytes(salt + salt));
        var hashBytes = k2.GetBytes(64); // 64 bytes = 512 bits
        var result = Convert.ToHexString(hashBytes);
        return result;
    }

    public static SaltHashed SaltHashNewPassword(string password)
    {
        var random = new Random();
        var salt = random.Next(10000, 99999);
        var salted = salt.ToString();
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